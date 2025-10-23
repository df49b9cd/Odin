using System.Text.Json;
using Odin.Contracts;
using Odin.Core;
using Odin.Persistence.Repositories;
using Shouldly;

namespace Odin.Integration.Tests;

[Collection("PostgresIntegration")]
public sealed class TaskQueueRepositoryTests(PostgresFixture fixture) : IAsyncLifetime
{
    private readonly PostgresFixture _fixture = fixture;
    private TaskQueueRepository? _taskQueueRepository;
    private NamespaceRepository? _namespaceRepository;
    private Guid _namespaceId;
    private string _queueName = string.Empty;

    public async ValueTask InitializeAsync()
    {
        _fixture.EnsureDockerIsRunning();
        _taskQueueRepository ??= _fixture.CreateTaskQueueRepository();
        _namespaceRepository ??= _fixture.CreateNamespaceRepository();

        await _fixture.ResetDatabaseAsync();

        _queueName = $"queue-{Guid.NewGuid():N}";
        var createdNamespace = await _namespaceRepository!.CreateAsync(
            new CreateNamespaceRequest { NamespaceName = $"ns-{Guid.NewGuid():N}" },
            TestContext.Current.CancellationToken);

        createdNamespace.IsSuccess.ShouldBeTrue(createdNamespace.Error?.Message ?? "Failed to create namespace");
        _namespaceId = createdNamespace.Value.NamespaceId;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task EnqueuePollAndComplete_RemovesTaskFromQueue()
    {
        var task = CreateTaskQueueItem(taskId: 1);
        var enqueue = await Repository.EnqueueAsync(task, TestContext.Current.CancellationToken);
        enqueue.IsSuccess.ShouldBeTrue(enqueue.Error?.Message ?? "EnqueueAsync failed");

        var poll = await Repository.PollAsync(_queueName, "worker-a", TestContext.Current.CancellationToken);
        poll.IsSuccess.ShouldBeTrue(poll.Error?.Message ?? "PollAsync failed");
        poll.Value.ShouldNotBeNull();

        var complete = await Repository.CompleteAsync(poll.Value!.LeaseId, TestContext.Current.CancellationToken);
        complete.IsSuccess.ShouldBeTrue(complete.Error?.Message ?? "CompleteAsync failed");

        var nextPoll = await Repository.PollAsync(_queueName, "worker-a", TestContext.Current.CancellationToken);
        nextPoll.IsSuccess.ShouldBeTrue(nextPoll.Error?.Message ?? "Second PollAsync failed");
        nextPoll.Value.ShouldBeNull();
    }

    [Fact]
    public async Task FailAsync_RequeuesTaskForAnotherWorker()
    {
        var task = CreateTaskQueueItem(taskId: 99);
        await Repository.EnqueueAsync(task, TestContext.Current.CancellationToken);

        var poll = await Repository.PollAsync(_queueName, "worker-b", TestContext.Current.CancellationToken);
        poll.Value.ShouldNotBeNull();

        var fail = await Repository.FailAsync(
            poll.Value!.LeaseId,
            "failure",
            requeue: true,
            TestContext.Current.CancellationToken);
        fail.IsSuccess.ShouldBeTrue(fail.Error?.Message ?? "FailAsync failed");

        var repoll = await Repository.PollAsync(_queueName, "worker-c", TestContext.Current.CancellationToken);
        repoll.IsSuccess.ShouldBeTrue(repoll.Error?.Message ?? "PollAsync after fail failed");
        repoll.Value.ShouldNotBeNull();
        repoll.Value!.Task.TaskId.ShouldBe(task.TaskId);
    }

    private TaskQueueItem CreateTaskQueueItem(long taskId)
        => new()
        {
            NamespaceId = _namespaceId,
            TaskQueueName = _queueName,
            TaskQueueType = TaskQueueType.Workflow,
            TaskId = taskId,
            WorkflowId = $"workflow-{taskId}",
            RunId = Guid.NewGuid(),
            ScheduledAt = DateTimeOffset.UtcNow,
            ExpiryAt = DateTimeOffset.UtcNow.AddMinutes(5),
            TaskData = JsonDocument.Parse("""{"payload":true}"""),
            PartitionHash = HashingUtilities.CalculatePartitionHash(_queueName)
        };

    private TaskQueueRepository Repository
        => _taskQueueRepository ?? throw new InvalidOperationException("Task queue repository was not initialized.");
}
