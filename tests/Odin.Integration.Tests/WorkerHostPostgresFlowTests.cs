extern alias api;

using System.Net.Http.Json;
using System.Text.Json;
using Hugo;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Odin.Contracts;
using Odin.Core;
using Odin.ExecutionEngine.Matching;
using Odin.Persistence;
using Odin.Sdk;
using Odin.WorkerHost;
using OrderProcessing.Shared;
using Shouldly;
using static Hugo.Go;
using ApiProgram = api::Odin.ControlPlane.Api.Program;

namespace Odin.Integration.Tests;

[Collection("PostgresIntegration")]
public sealed class WorkerHostPostgresFlowTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    [Fact]
    public async Task WorkerHost_ProcessesTasksAgainstPostgres_AndApiReportsQueueDepth()
    {
        _fixture.EnsureDockerIsRunning();
        await _fixture.ResetDatabaseAsync();

        var namespaceId = await _fixture.CreateNamespaceAsync("worker-host-int");
        var probe = new TestProbe(targetCount: 3);

        using var host = BuildWorkerHost(_fixture.ConnectionString, namespaceId, probe);
        await host.StartAsync(TestContext.Current.CancellationToken);

        try
        {
            using var apiFactory = new PostgresApiFactory(_fixture.ConnectionString);
            using var client = apiFactory.CreateClient();

            await probe.WaitForCompletionAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            await Task.Delay(TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken);

            var response = await client.GetAsync("/api/v1/tasks/queues/orders/stats", TestContext.Current.CancellationToken);
            response.EnsureSuccessStatusCode();

            var stats = await response.Content.ReadFromJsonAsync<QueueStats>(cancellationToken: TestContext.Current.CancellationToken);
            stats.ShouldNotBeNull();
            stats!.QueueName.ShouldBe("orders");
            stats.PendingTasks.ShouldBe(0);
        }
        finally
        {
            await host.StopAsync(TestContext.Current.CancellationToken);
            host.Dispose();
        }
    }

    private static IHost BuildWorkerHost(string connectionString, Guid namespaceId, TestProbe probe)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = "IntegrationTest"
        });

        builder.Services.AddLogging();

        builder.Services.AddPersistence(options =>
        {
            options.UsePostgreSql(connectionString);
        });

        builder.Services.AddSingleton<IMatchingService, MatchingService>();
        builder.Services.AddWorkflowRuntime();
        builder.Services.AddWorkflow<TestWorkflow, TestWorkflowInput, OrderResult>("order-processing");
        builder.Services.AddSingleton(probe);
        builder.Services.AddSingleton(new TestWorkerConfig(namespaceId, TaskCount: 3));
        builder.Services.AddHostedService<TestSeeder>();
        builder.Services.AddHostedService<Worker>();

        return builder.Build();
    }

    private sealed record TestWorkerConfig(Guid NamespaceId, int TaskCount);

    private sealed record TestWorkflowInput(string OrderId, decimal Amount);

    private sealed class TestProbe
    {
        private readonly int _targetCount;
        private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _count;

        public TestProbe(int targetCount)
        {
            if (targetCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(targetCount));
            }

            _targetCount = targetCount;
        }

        public void Record()
        {
            if (Interlocked.Increment(ref _count) >= _targetCount)
            {
                _completion.TrySetResult(true);
            }
        }

        public Task WaitForCompletionAsync(TimeSpan timeout, CancellationToken cancellationToken)
            => _completion.Task.WaitAsync(timeout, cancellationToken);
    }

    private sealed class TestWorkflow(TestProbe probe) : WorkflowBase<TestWorkflowInput, OrderResult>
    {
        protected override Task<Result<OrderResult>> ExecuteAsync(
            WorkflowExecutionContext context,
            TestWorkflowInput input,
            CancellationToken cancellationToken)
        {
            probe.Record();

            var result = new OrderResult(
                OrderId: input.OrderId,
                Status: "Processed",
                TransactionId: $"txn-{Guid.NewGuid():N}",
                Amount: input.Amount);

            return Task.FromResult(Result.Ok(result));
        }
    }

    private sealed class TestSeeder(
        IMatchingService matchingService,
        TestWorkerConfig config,
        TestProbe probe,
        ILogger<TestSeeder> logger) : BackgroundService
    {
        private static readonly JsonSerializerOptions SerializerOptions = JsonOptions.Default;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            for (var index = 0; index < config.TaskCount && !stoppingToken.IsCancellationRequested; index++)
            {
                var orderId = $"ORD-{index + 1:0000}";
                var runId = Guid.NewGuid();
                var queueName = "orders";
                var payload = new WorkflowTask(
                    Namespace: "integration",
                    WorkflowId: orderId,
                    RunId: runId.ToString("N"),
                    TaskQueue: queueName,
                    WorkflowType: "order-processing",
                    Input: new TestWorkflowInput(orderId, 100 + index),
                    Metadata: new Dictionary<string, string>
                    {
                        ["source"] = "integration-test"
                    },
                    StartedAt: DateTimeOffset.UtcNow);

                var item = new TaskQueueItem
                {
                    NamespaceId = config.NamespaceId,
                    TaskQueueName = queueName,
                    TaskQueueType = TaskQueueType.Workflow,
                    TaskId = index + 1,
                    WorkflowId = orderId,
                    RunId = runId,
                    ScheduledAt = DateTimeOffset.UtcNow,
                    ExpiryAt = null,
                    TaskData = JsonSerializer.SerializeToDocument(payload, SerializerOptions),
                    PartitionHash = HashingUtilities.CalculatePartitionHash(queueName)
                };

                var enqueue = await matchingService.EnqueueTaskAsync(item, stoppingToken).ConfigureAwait(false);
                if (enqueue.IsFailure)
                {
                    logger.LogWarning("Failed to enqueue task {OrderId}: {Error}", orderId, enqueue.Error?.Message);
                }
            }

            try
            {
                await probe.WaitForCompletionAsync(TimeSpan.FromSeconds(20), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Ignored during shutdown.
            }
        }
    }

    private sealed class PostgresApiFactory(string connectionString) : WebApplicationFactory<ApiProgram>
    {
        private readonly string _connectionString = connectionString;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("IntegrationTest");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                var overrides = new Dictionary<string, string?>
                {
                    ["Persistence:Provider"] = PersistenceProvider.PostgreSql.ToString(),
                    ["Persistence:ConnectionString"] = _connectionString,
                    ["Grpc:WorkflowService:Address"] = "http://127.0.0.1"
                };

                configuration.AddInMemoryCollection(overrides!);
            });
        }
    }
}
