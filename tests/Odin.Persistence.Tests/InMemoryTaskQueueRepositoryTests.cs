using System.Text.Json;
using System.Threading;
using Odin.Contracts;
using Odin.Persistence.InMemory;
using Shouldly;
using Xunit;

namespace Odin.Persistence.Tests;

public class InMemoryTaskQueueRepositoryTests
{
    [Fact]
    public async Task EnqueuePollHeartbeatAndComplete_WorkflowTaskLifecycle()
    {
        await using var repository = new InMemoryTaskQueueRepository();
        var task = CreateTaskQueueItem(taskId: 1);
        var ct = TestContext.Current.CancellationToken;

        var enqueue = await repository.EnqueueAsync(task, ct);
        enqueue.IsSuccess.ShouldBeTrue(enqueue.Error?.Message ?? "EnqueueAsync failed");

        var leaseResult = await repository.PollAsync(task.TaskQueueName, "worker-1", ct);
        leaseResult.IsSuccess.ShouldBeTrue(leaseResult.Error?.Message ?? "PollAsync failed");
        leaseResult.Value.ShouldNotBeNull();

        var heartbeat = await repository.HeartbeatAsync(leaseResult.Value!.LeaseId, ct);
        heartbeat.IsSuccess.ShouldBeTrue(heartbeat.Error?.Message ?? "HeartbeatAsync failed");

        var complete = await repository.CompleteAsync(leaseResult.Value.LeaseId, ct);
        complete.IsSuccess.ShouldBeTrue(complete.Error?.Message ?? "CompleteAsync failed");

        var depth = await repository.GetQueueDepthAsync(task.TaskQueueName, ct);
        depth.Value.ShouldBe(0);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));
        var nextPoll = await repository.PollAsync(task.TaskQueueName, "worker-1", cts.Token);
        nextPoll.IsSuccess.ShouldBeTrue(nextPoll.Error?.Message ?? "Second PollAsync failed");
        nextPoll.Value.ShouldBeNull();
    }

    [Fact]
    public async Task FailAsync_WithRequeue_MakesTaskAvailableAgain()
    {
        await using var repository = new InMemoryTaskQueueRepository();
        var task = CreateTaskQueueItem(taskId: 42);
        var ct = TestContext.Current.CancellationToken;

        await repository.EnqueueAsync(task, ct);
        var leaseResult = await repository.PollAsync(task.TaskQueueName, "worker-2", ct);
        leaseResult.Value.ShouldNotBeNull();

        var fail = await repository.FailAsync(leaseResult.Value!.LeaseId, "boom", requeue: true, cancellationToken: ct);
        fail.IsSuccess.ShouldBeTrue(fail.Error?.Message ?? "FailAsync failed");

        var retried = await repository.PollAsync(task.TaskQueueName, "worker-3", ct);
        retried.IsSuccess.ShouldBeTrue(retried.Error?.Message ?? "PollAsync after fail failed");
        retried.Value.ShouldNotBeNull();
        retried.Value!.Task.TaskId.ShouldBe(task.TaskId);
    }

    private static TaskQueueItem CreateTaskQueueItem(long taskId)
        => new()
        {
            NamespaceId = Guid.NewGuid(),
            TaskQueueName = "queue-" + Guid.NewGuid().ToString("N")[..8],
            TaskQueueType = TaskQueueType.Workflow,
            TaskId = taskId,
            WorkflowId = "workflow-" + Guid.NewGuid().ToString("N")[..8],
            RunId = Guid.NewGuid(),
            ScheduledAt = DateTimeOffset.UtcNow,
            ExpiryAt = DateTimeOffset.UtcNow.AddMinutes(5),
            TaskData = JsonDocument.Parse("""{"payload":"value"}""")
        };
}
