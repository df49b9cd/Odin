extern alias api;
extern alias grpc;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Grpc.Net.ClientFactory;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ApiProgram = api::Odin.ControlPlane.Api.Program;
using ApiSignalWorkflowRequest = api::Odin.ControlPlane.Api.Controllers.SignalWorkflowRequest;
using ApiStartWorkflowRequest = api::Odin.ControlPlane.Api.Controllers.StartWorkflowRequest;
using ApiStartWorkflowResponse = api::Odin.ControlPlane.Api.Controllers.StartWorkflowResponse;
using ApiTerminateWorkflowRequest = api::Odin.ControlPlane.Api.Controllers.TerminateWorkflowRequest;
using GrpcProgram = grpc::Odin.ControlPlane.Grpc.Program;
using GrpcWorkflowServiceClient = api::Odin.ControlPlane.Grpc.WorkflowService.WorkflowServiceClient;

namespace Odin.Integration.Tests;

internal sealed class GrpcServiceFactory : WebApplicationFactory<GrpcProgram>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("IntegrationTest");
    }
}

internal sealed class WorkflowApiFactory(GrpcServiceFactory grpcFactory) : WebApplicationFactory<ApiProgram>
{
    private readonly GrpcServiceFactory _grpcFactory = grpcFactory;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("IntegrationTest");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var overrides = new Dictionary<string, string?>
            {
                ["Grpc:WorkflowService:Address"] = _grpcFactory.Server.BaseAddress.ToString()
            };

            configuration.AddInMemoryCollection(overrides!);
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IConfigureOptions<GrpcClientFactoryOptions>>();

            services.AddGrpcClient<GrpcWorkflowServiceClient>(options =>
                {
                    options.Address = _grpcFactory.Server.BaseAddress;
                })
                .ConfigurePrimaryHttpMessageHandler(() => _grpcFactory.Server.CreateHandler());
        });
    }
}

public sealed class WorkflowFacadeFixture : IAsyncLifetime
{
    private GrpcServiceFactory GrpcFactory { get; } = new();

    private WorkflowApiFactory? _apiFactory;

    public HttpClient ApiClient { get; private set; } = null!;

    private HttpClient? _grpcClient;

    public ValueTask InitializeAsync()
    {
        _grpcClient = GrpcFactory.CreateDefaultClient();
        _apiFactory = new WorkflowApiFactory(GrpcFactory);
        ApiClient = _apiFactory.CreateClient();

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        ApiClient.Dispose();
        _apiFactory?.Dispose();
        _grpcClient?.Dispose();
        GrpcFactory.Dispose();

        return ValueTask.CompletedTask;
    }
}

[CollectionDefinition("WorkflowFacade collection", DisableParallelization = true)]
public sealed class WorkflowFacadeCollection : ICollectionFixture<WorkflowFacadeFixture>;

[Collection("WorkflowFacade collection")]
public sealed class RestWorkflowFacadeTests(WorkflowFacadeFixture fixture)
{
    private readonly HttpClient _client = fixture.ApiClient;

    [Fact]
    public async Task StartWorkflow_And_GetWorkflow_RoundTripsThroughGrpc()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var namespaceId = Guid.NewGuid().ToString();

        var request = new ApiStartWorkflowRequest
        {
            NamespaceId = namespaceId,
            WorkflowType = "order-processing",
            TaskQueue = "integration-tests"
        };

        var response = await _client.PostAsJsonAsync("/api/v1/workflows/start", request, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var start = await response.Content.ReadFromJsonAsync<ApiStartWorkflowResponse>(cancellationToken);

        Assert.NotNull(start);
        Assert.False(string.IsNullOrWhiteSpace(start!.WorkflowId));
        Assert.False(string.IsNullOrWhiteSpace(start.RunId));

        var getResponse = await _client.GetAsync($"/api/v1/workflows/{start.WorkflowId}?namespaceId={namespaceId}&runId={start.RunId}", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var execution = await getResponse.Content.ReadFromJsonAsync<Odin.Contracts.WorkflowExecution>(cancellationToken);
        Assert.NotNull(execution);
        Assert.Equal(start.WorkflowId, execution!.WorkflowId);
        Assert.Equal(Guid.Parse(start.RunId), execution.RunId);
        Assert.Equal("order-processing", execution.WorkflowType);
    }

    [Fact]
    public async Task SignalWorkflow_PassesThroughFacade()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var namespaceId = Guid.NewGuid().ToString();

        var startRequest = new ApiStartWorkflowRequest
        {
            NamespaceId = namespaceId,
            WorkflowType = "order-processing",
            TaskQueue = "integration-tests"
        };

        var startResponse = await _client.PostAsJsonAsync("/api/v1/workflows/start", startRequest, cancellationToken);
        var payload = await startResponse.Content.ReadFromJsonAsync<ApiStartWorkflowResponse>(cancellationToken);
        Assert.NotNull(payload);

        var signalRequest = new ApiSignalWorkflowRequest
        {
            NamespaceId = namespaceId,
            SignalName = "test-signal"
        };

        var signalResponse = await _client.PostAsJsonAsync($"/api/v1/workflows/{payload!.WorkflowId}/signal", signalRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, signalResponse.StatusCode);
    }

    [Fact]
    public async Task TerminateWorkflow_UpdatesState()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var namespaceId = Guid.NewGuid().ToString();

        var startRequest = new ApiStartWorkflowRequest
        {
            NamespaceId = namespaceId,
            WorkflowType = "order-processing",
            TaskQueue = "integration-tests"
        };

        var startResponse = await _client.PostAsJsonAsync("/api/v1/workflows/start", startRequest, cancellationToken);
        var payload = await startResponse.Content.ReadFromJsonAsync<ApiStartWorkflowResponse>(cancellationToken);
        Assert.NotNull(payload);

        var terminateRequest = new ApiTerminateWorkflowRequest
        {
            NamespaceId = namespaceId,
            Reason = "integration-test"
        };

        var terminateResponse = await _client.PostAsJsonAsync($"/api/v1/workflows/{payload!.WorkflowId}/terminate", terminateRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, terminateResponse.StatusCode);

        var getResponse = await _client.GetAsync($"/api/v1/workflows/{payload.WorkflowId}?namespaceId={namespaceId}&runId={payload.RunId}", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var execution = await getResponse.Content.ReadFromJsonAsync<Odin.Contracts.WorkflowExecution>(cancellationToken);
        Assert.NotNull(execution);
        Assert.Equal(Odin.Contracts.WorkflowState.Terminated, execution!.WorkflowState);
    }
}
