using System.Text.Json;
using Hugo;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Odin.Contracts;
using Odin.Core;
using Odin.ExecutionEngine.Matching;
using Odin.Persistence.InMemory;
using Odin.Persistence.Interfaces;
using Odin.Sdk;
using Shouldly;
using static Hugo.Go;

namespace Odin.ExecutionEngine.Tests;

public sealed class MatchingServiceTests
{
    [Fact]
    public async Task SubscribeAsync_DeliversTasks_AndCompletesThroughRepository()
    {
        await using var repository = new InMemoryTaskQueueRepository();
        var service = new MatchingService(repository, NullLogger<MatchingService>.Instance);

        var queueName = "unit-tests";
        var task = CreateTaskQueueItem(queueName, 1);

        var enqueueResult = await service.EnqueueTaskAsync(task, TestContext.Current.CancellationToken);
        enqueueResult.IsSuccess.ShouldBeTrue(enqueueResult.Error?.Message ?? "EnqueueAsync failed");

        await using var subscription = await service.SubscribeAsync(queueName, "worker-1", TestContext.Current.CancellationToken);

        var matchingTask = await subscription.Reader.ReadAsync(TestContext.Current.CancellationToken);

        matchingTask.WorkflowTask.WorkflowId.ShouldBe(task.WorkflowId);
        matchingTask.Lease.LeaseId.ShouldNotBe(Guid.Empty);

        var completion = await matchingTask.CompleteAsync(TestContext.Current.CancellationToken);
        completion.IsSuccess.ShouldBeTrue(completion.Error?.Message ?? "CompleteAsync failed");

        var depth = await repository.GetQueueDepthAsync(queueName, TestContext.Current.CancellationToken);
        depth.IsSuccess.ShouldBeTrue(depth.Error?.Message ?? "GetQueueDepthAsync failed");
        depth.Value.ShouldBe(0);
    }

    [Fact]
    public async Task SubscribeAsync_WhenDispatchItemCreationFails_MarksLeaseAsFailed()
    {
        var repository = Substitute.For<ITaskQueueRepository>();
        var logger = NullLogger<MatchingService>.Instance;
        var service = new MatchingService(repository, logger);

        var leaseId = Guid.NewGuid();
        var lease = new TaskLease
        {
            LeaseId = leaseId,
            WorkerIdentity = "worker-lease",
            LeasedAt = DateTimeOffset.UtcNow,
            LeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1),
            Task = new TaskQueueItem
            {
                NamespaceId = Guid.NewGuid(),
                TaskQueueName = "broken-queue",
                TaskQueueType = TaskQueueType.Workflow,
                TaskId = 42,
                WorkflowId = "wf-broken",
                RunId = Guid.NewGuid(),
                ScheduledAt = DateTimeOffset.UtcNow,
                ExpiryAt = null,
                TaskData = JsonDocument.Parse("null"),
                PartitionHash = 0
            }
        };

        _ = repository.PollAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(Result.Ok<TaskLease?>(lease)),
                Task.FromResult(Result.Ok<TaskLease?>(null)));

        _ = repository.HeartbeatAsync(leaseId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(lease)));

        var failSignal = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        _ = repository.FailAsync(leaseId, Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var reason = callInfo.ArgAt<string>(1);
                _ = failSignal.TrySetResult(reason);
                return Task.FromResult(Result.Ok(Unit.Value));
            });

        await using var subscription = await service.SubscribeAsync("broken-queue", "worker-broken", TestContext.Current.CancellationToken);
        var reason = await failSignal.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        reason.ShouldBe("Dispatch item creation failed");
        _ = await repository.Received(1).FailAsync(leaseId, "Dispatch item creation failed", false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReclaimExpiredLeasesAsync_RequeuesExpiredItems()
    {
        var options = new TaskQueueOptions
        {
            Capacity = 32,
            LeaseDuration = TimeSpan.FromMilliseconds(100),
            HeartbeatInterval = TimeSpan.FromMilliseconds(50),
            LeaseSweepInterval = TimeSpan.FromMilliseconds(50),
            RequeueDelay = TimeSpan.FromMilliseconds(10),
            MaxDeliveryAttempts = 5
        };

        await using var repository = new InMemoryTaskQueueRepository(options);
        var service = new MatchingService(repository, NullLogger<MatchingService>.Instance);

        var queueName = "lease-reclaim";
        var ct = TestContext.Current.CancellationToken;
        const int taskCount = 10;

        for (var i = 0; i < taskCount; i++)
        {
            _ = await service.EnqueueTaskAsync(CreateTaskQueueItem(queueName, i), ct);
        }

        var leases = new List<TaskLease>();
        for (var i = 0; i < taskCount; i++)
        {
            var leaseResult = await repository.PollAsync(queueName, $"worker-{i}", ct);
            leaseResult.IsSuccess.ShouldBeTrue(leaseResult.Error?.Message ?? "PollAsync failed");
            _ = leaseResult.Value.ShouldNotBeNull();
            leases.Add(leaseResult.Value!);
        }

        await Task.Delay(TimeSpan.FromMilliseconds(200), ct);

        var reclaimed = await service.ReclaimExpiredLeasesAsync(ct);
        reclaimed.IsSuccess.ShouldBeTrue(reclaimed.Error?.Message ?? "ReclaimExpiredLeasesAsync failed");
        reclaimed.Value.ShouldBe(taskCount);

        var replayLease = await service.PollTaskAsync(queueName, "reclaim-worker", TimeSpan.FromSeconds(1), ct);
        replayLease.IsSuccess.ShouldBeTrue(replayLease.Error?.Message ?? "PollTaskAsync failed");
        _ = replayLease.Value.ShouldNotBeNull();
    }

    private static TaskQueueItem CreateTaskQueueItem(string queueName, long taskId)
    {
        var workflowId = $"wf-{queueName}-{taskId:000}";
        var runId = Guid.NewGuid();
        var payload = new WorkflowTask(
            Namespace: "default",
            WorkflowId: workflowId,
            RunId: runId.ToString("N"),
            TaskQueue: queueName,
            WorkflowType: "unit-test",
            Input: new { taskId },
            Metadata: new Dictionary<string, string> { ["source"] = "matching-tests" },
            StartedAt: DateTimeOffset.UtcNow);

        return new TaskQueueItem
        {
            NamespaceId = Guid.NewGuid(),
            TaskQueueName = queueName,
            TaskQueueType = TaskQueueType.Workflow,
            TaskId = taskId,
            WorkflowId = workflowId,
            RunId = runId,
            ScheduledAt = DateTimeOffset.UtcNow,
            ExpiryAt = null,
            TaskData = JsonSerializer.SerializeToDocument(payload, JsonOptions.Default),
            PartitionHash = HashingUtilities.CalculatePartitionHash(queueName)
        };
    }
}
