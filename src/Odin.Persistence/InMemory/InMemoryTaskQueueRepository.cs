using System.Collections.Concurrent;
using Hugo;
using Odin.Contracts;
using Odin.Core;
using Odin.Persistence.Interfaces;
using static Hugo.Go;

namespace Odin.Persistence.InMemory;

/// <summary>
/// In-memory task queue repository with basic leasing semantics for local development.
/// </summary>
public sealed class InMemoryTaskQueueRepository : ITaskQueueRepository
{
    private readonly ConcurrentDictionary<string, QueueState> _queues = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Guid, LeaseState> _leases = new();

    public Task<Result<Guid>> EnqueueAsync(
        TaskQueueItem task,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);

        var queue = _queues.GetOrAdd(task.TaskQueueName, name => new QueueState(name));
        var now = DateTimeOffset.UtcNow;
        var pendingTask = new PendingTask(Guid.NewGuid(), task, 1, now);

        queue.Pending.Enqueue(pendingTask);

        return Task.FromResult(Result.Ok(pendingTask.TaskId));
    }

    public Task<Result<TaskLease?>> PollAsync(
        string queueName,
        string workerIdentity,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerIdentity);

        if (!_queues.TryGetValue(queueName, out var queue))
        {
            return Task.FromResult(Result.Ok<TaskLease?>(null));
        }

        while (queue.Pending.TryDequeue(out var pending))
        {
            if (pending.Item.ExpiryAt is { } expiry && expiry <= DateTimeOffset.UtcNow)
            {
                // Drop expired task and continue polling the next one.
                continue;
            }

            var now = DateTimeOffset.UtcNow;
            var lease = new TaskLease
            {
                LeaseId = Guid.NewGuid(),
                Task = pending.Item,
                WorkerIdentity = workerIdentity,
                LeasedAt = now,
                LeaseExpiresAt = now.Add(leaseDuration),
                HeartbeatAt = now,
                AttemptCount = pending.Attempt
            };

            _leases[lease.LeaseId] = new LeaseState(pending.TaskId, queueName, lease);

            return Task.FromResult(Result.Ok<TaskLease?>(lease));
        }

        return Task.FromResult(Result.Ok<TaskLease?>(null));
    }

    public Task<Result<TaskLease>> HeartbeatAsync(
        Guid leaseId,
        TimeSpan extendBy,
        CancellationToken cancellationToken = default)
    {
        if (!_leases.TryGetValue(leaseId, out var leaseState))
        {
            return Task.FromResult(Result.Fail<TaskLease>(
                Error.From($"Lease {leaseId} not found", OdinErrorCodes.TaskLeaseExpired)));
        }

        var now = DateTimeOffset.UtcNow;
        var extendedLease = leaseState.Lease with
        {
            LeaseExpiresAt = now.Add(extendBy),
            HeartbeatAt = now
        };

        _leases[leaseId] = leaseState with { Lease = extendedLease };

        return Task.FromResult(Result.Ok(extendedLease));
    }

    public Task<Result<Unit>> CompleteAsync(
        Guid taskId,
        Guid leaseId,
        CancellationToken cancellationToken = default)
    {
        if (!_leases.TryGetValue(leaseId, out var leaseState) || leaseState.TaskId != taskId)
        {
            return Task.FromResult(Result.Fail<Unit>(
                Error.From($"Task {taskId} not found for lease {leaseId}", OdinErrorCodes.TaskNotFound)));
        }

        _leases.TryRemove(leaseId, out _);

        return Task.FromResult(Result.Ok(Unit.Value));
    }

    public Task<Result<Unit>> FailAsync(
        Guid taskId,
        Guid leaseId,
        string reason,
        bool requeue = true,
        CancellationToken cancellationToken = default)
    {
        if (!_leases.TryRemove(leaseId, out var leaseState) || leaseState.TaskId != taskId)
        {
            return Task.FromResult(Result.Fail<Unit>(
                Error.From($"Task {taskId} not found for lease {leaseId}", OdinErrorCodes.TaskNotFound)));
        }

        if (requeue)
        {
            var queue = _queues.GetOrAdd(leaseState.QueueName, name => new QueueState(name));
            var now = DateTimeOffset.UtcNow;
            var task = leaseState.Lease.Task;
            var pending = new PendingTask(Guid.NewGuid(), task, leaseState.Lease.AttemptCount + 1, now);
            queue.Pending.Enqueue(pending);
        }

        return Task.FromResult(Result.Ok(Unit.Value));
    }

    public Task<Result<int>> GetQueueDepthAsync(
        string queueName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);

        if (!_queues.TryGetValue(queueName, out var queue))
        {
            return Task.FromResult(Result.Ok(0));
        }

        return Task.FromResult(Result.Ok(queue.Pending.Count));
    }

    public Task<Result<Dictionary<string, int>>> ListQueuesAsync(
        string? namespaceId = null,
        CancellationToken cancellationToken = default)
    {
        var snapshot = _queues.ToArray();
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, queue) in snapshot)
        {
            result[name] = queue.Pending.Count;
        }

        return Task.FromResult(Result.Ok(result));
    }

    public Task<Result<int>> ReclaimExpiredLeasesAsync(
        CancellationToken cancellationToken = default)
    {
        var reclaimed = 0;
        var now = DateTimeOffset.UtcNow;

        foreach (var (leaseId, leaseState) in _leases.ToArray())
        {
            if (leaseState.Lease.LeaseExpiresAt > now)
            {
                continue;
            }

            if (_leases.TryRemove(leaseId, out var removed))
            {
                reclaimed++;

                var queue = _queues.GetOrAdd(removed.QueueName, name => new QueueState(name));
                var pending = new PendingTask(Guid.NewGuid(), removed.Lease.Task, removed.Lease.AttemptCount + 1, now);
                queue.Pending.Enqueue(pending);
            }
        }

        return Task.FromResult(Result.Ok(reclaimed));
    }

    public Task<Result<int>> PurgeOldTasksAsync(
        DateTimeOffset olderThan,
        CancellationToken cancellationToken = default)
    {
        var removed = 0;

        foreach (var (_, queue) in _queues)
        {
            var surviving = new List<PendingTask>();

            while (queue.Pending.TryDequeue(out var pending))
            {
                if (pending.EnqueuedAt < olderThan)
                {
                    removed++;
                    continue;
                }

                surviving.Add(pending);
            }

            foreach (var pending in surviving)
            {
                queue.Pending.Enqueue(pending);
            }
        }

        return Task.FromResult(Result.Ok(removed));
    }

    private sealed record PendingTask(Guid TaskId, TaskQueueItem Item, int Attempt, DateTimeOffset EnqueuedAt);

    private sealed record QueueState(string Name)
    {
        public ConcurrentQueue<PendingTask> Pending { get; } = new();
    }

    private sealed record LeaseState(Guid TaskId, string QueueName, TaskLease Lease);
}
