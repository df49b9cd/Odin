using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Hugo;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Odin.Contracts;
using Odin.Core;
using Odin.ExecutionEngine.Matching;
using Odin.ExecutionEngine.SystemWorkers.Services;
using Odin.Persistence.Interfaces;
using Odin.Sdk;
using Shouldly;
using static Hugo.Go;

namespace Odin.ExecutionEngine.Tests;

public sealed class SystemWorkerTests
{
    [Fact]
    public async Task RetryWorker_FailsTaskAndRequestsRequeue()
    {
        var repository = new TestTaskQueueRepository();
        var matchingService = new MatchingService(repository, NullLogger<MatchingService>.Instance);
        _ = await matchingService.EnqueueTaskAsync(CreateQueueItem("system:retries", taskId: 1), TestContext.Current.CancellationToken);

        var worker = new RetryWorker(matchingService, NullLogger<RetryWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);

        try
        {
            var failure = await repository.Failed.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            failure.LeaseId.ShouldNotBe(Guid.Empty);
            failure.Requeue.ShouldBeTrue();
            failure.Reason.ShouldBe("retry-scheduled");
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task TimerWorker_CompletesTimerTasks()
    {
        var repository = new TestTaskQueueRepository();
        var matchingService = new MatchingService(repository, NullLogger<MatchingService>.Instance);
        _ = await matchingService.EnqueueTaskAsync(CreateQueueItem("system:timers", taskId: 7), TestContext.Current.CancellationToken);

        var worker = new TimerWorker(matchingService, NullLogger<TimerWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);

        try
        {
            var completedLease = await repository.Completed.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            completedLease.ShouldNotBe(Guid.Empty);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task CleanupWorker_InvokesLeaseReclaimAndPurge()
    {
        var matchingService = Substitute.For<IMatchingService>();
        var repository = Substitute.For<ITaskQueueRepository>();

        var reclaimSignal = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var purgeSignal = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        _ = matchingService.ReclaimExpiredLeasesAsync(Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _ = reclaimSignal.TrySetResult(1);
                return Task.FromResult(Result.Ok(5));
            });

        _ = repository.PurgeOldTasksAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _ = purgeSignal.TrySetResult(1);
                return Task.FromResult(Result.Ok(3));
            });

        var worker = new CleanupWorker(matchingService, repository, NullLogger<CleanupWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);

        try
        {
            _ = await Task.WhenAll(
                reclaimSignal.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken),
                purgeSignal.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        _ = await matchingService.Received().ReclaimExpiredLeasesAsync(Arg.Any<CancellationToken>());
        _ = await repository.Received().PurgeOldTasksAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    private static TaskQueueItem CreateQueueItem(string queueName, long taskId)
    {
        var workflowId = $"{queueName}-wf-{taskId}";
        var runId = Guid.NewGuid();
        var payload = new WorkflowTask(
            Namespace: "system",
            WorkflowId: workflowId,
            RunId: runId.ToString("N"),
            TaskQueue: queueName,
            WorkflowType: queueName,
            Input: new { taskId },
            Metadata: new Dictionary<string, string> { ["system"] = "true" },
            StartedAt: DateTimeOffset.UtcNow);

        return new TaskQueueItem
        {
            NamespaceId = Guid.Parse("00000000-0000-0000-0000-0000000000AB"),
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

    private sealed class TestTaskQueueRepository : ITaskQueueRepository
    {
        private readonly Channel<TaskLease> _leases = Channel.CreateUnbounded<TaskLease>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });

        private readonly ConcurrentDictionary<Guid, TaskLease> _leaseLookup = new();

        public TaskCompletionSource<(Guid LeaseId, bool Requeue, string Reason)> Failed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<Guid> Completed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<Result<Guid>> EnqueueAsync(TaskQueueItem task, CancellationToken cancellationToken = default)
        {
            var lease = new TaskLease
            {
                LeaseId = Guid.NewGuid(),
                WorkerIdentity = string.Empty,
                LeasedAt = DateTimeOffset.UtcNow,
                LeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
                Task = task
            };

            _leaseLookup[lease.LeaseId] = lease;
            _ = _leases.Writer.TryWrite(lease);
            return Task.FromResult(Result.Ok(lease.LeaseId));
        }

        public async Task<Result<TaskLease?>> PollAsync(string queueName, string workerIdentity, CancellationToken cancellationToken = default)
        {
            while (await _leases.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (_leases.Reader.TryRead(out var lease))
                {
                    var assigned = lease with { WorkerIdentity = workerIdentity };
                    _leaseLookup[assigned.LeaseId] = assigned;
                    return Result.Ok<TaskLease?>(assigned);
                }
            }

            return Result.Ok<TaskLease?>(null);
        }

        public Task<Result<TaskLease>> HeartbeatAsync(Guid leaseId, CancellationToken cancellationToken = default)
        {
            if (_leaseLookup.TryGetValue(leaseId, out var lease))
            {
                return Task.FromResult(Result.Ok(lease));
            }

            return Task.FromResult(Result.Fail<TaskLease>(
                Error.From($"Lease {leaseId} not found.", OdinErrorCodes.TaskLeaseExpired)));
        }

        public Task<Result<Unit>> CompleteAsync(Guid leaseId, CancellationToken cancellationToken = default)
        {
            _ = Completed.TrySetResult(leaseId);
            _ = _leaseLookup.TryRemove(leaseId, out _);
            return Task.FromResult(Result.Ok(Unit.Value));
        }

        public Task<Result<Unit>> FailAsync(Guid leaseId, string reason, bool requeue = true, CancellationToken cancellationToken = default)
        {
            _ = Failed.TrySetResult((leaseId, requeue, reason));
            _ = _leaseLookup.TryRemove(leaseId, out _);
            return Task.FromResult(Result.Ok(Unit.Value));
        }

        public Task<Result<int>> GetQueueDepthAsync(string queueName, CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Ok(0));

        public Task<Result<Dictionary<string, int>>> ListQueuesAsync(string? namespaceId = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Ok(new Dictionary<string, int>()));

        public Task<Result<int>> ReclaimExpiredLeasesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Ok(0));

        public Task<Result<int>> PurgeOldTasksAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Ok(0));
    }
}
