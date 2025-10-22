using System.Collections.Concurrent;
using System.Threading.Channels;
using Hugo;
using Odin.Contracts;
using Odin.Core;
using Odin.Persistence.Interfaces;
using static Hugo.Go;

namespace Odin.Persistence.InMemory;

/// <summary>
/// In-memory task queue repository backed by <see cref="Hugo.TaskQueue{T}"/>.
/// Provides cooperative leasing semantics with automatic expiry handling.
/// </summary>
public sealed class InMemoryTaskQueueRepository : ITaskQueueRepository, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, QueueContext> _queues = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Guid, LeaseHandle> _leases = new();
    private readonly TimeProvider _timeProvider;
    private readonly OptionsSnapshot _defaultOptions;

    public InMemoryTaskQueueRepository()
        : this(options: null, timeProvider: null)
    {
    }

    public InMemoryTaskQueueRepository(TaskQueueOptions? options, TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _defaultOptions = new OptionsSnapshot(options ?? new TaskQueueOptions());
    }

    public async Task<Result<Guid>> EnqueueAsync(
        TaskQueueItem task,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);

        var context = GetOrCreateQueue(task.TaskQueueName);
        var entry = new QueueEntry(Guid.NewGuid(), task);

        try
        {
            await context.Queue.EnqueueAsync(entry, cancellationToken).ConfigureAwait(false);
            return Result.Ok(entry.InstanceId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result.Fail<Guid>(Error.Canceled(token: cancellationToken));
        }
        catch (Exception ex)
        {
            return Result.Fail<Guid>(
                Error.From($"Failed to enqueue task: {ex.Message}", OdinErrorCodes.TaskQueueError));
        }
    }

    public async Task<Result<TaskLease?>> PollAsync(
        string queueName,
        string workerIdentity,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerIdentity);

        var context = GetOrCreateQueue(queueName);
        var reader = context.Reader;

        try
        {
            while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!reader.TryRead(out var lease))
                {
                    continue;
                }

                var leasedAt = _timeProvider.GetUtcNow();
                var leaseId = Guid.NewGuid();
                var handle = new LeaseHandle(context, workerIdentity, leaseId, lease, leasedAt);

                if (!_leases.TryAdd(leaseId, handle))
                {
                    await lease.FailAsync(
                            Error.From("Lease registration conflict.", OdinErrorCodes.TaskQueueError),
                            requeue: true,
                            cancellationToken: CancellationToken.None)
                        .ConfigureAwait(false);

                    return Result.Fail<TaskLease?>(
                        Error.From("Failed to register lease handle.", OdinErrorCodes.TaskQueueError));
                }

                return Result.Ok<TaskLease?>(CreateContractLease(handle));
            }

            return Result.Ok<TaskLease?>(null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result.Ok<TaskLease?>(null);
        }
        catch (ObjectDisposedException)
        {
            return Result.Ok<TaskLease?>(null);
        }
        catch (Exception ex)
        {
            return Result.Fail<TaskLease?>(
                Error.From($"Poll failed: {ex.Message}", OdinErrorCodes.TaskQueueError));
        }
    }

    public async Task<Result<TaskLease>> HeartbeatAsync(
        Guid leaseId,
        CancellationToken cancellationToken = default)
    {
        if (!_leases.TryGetValue(leaseId, out var handle))
        {
            return Result.Fail<TaskLease>(
                Error.From($"Lease {leaseId} not found.", OdinErrorCodes.TaskLeaseExpired));
        }

        try
        {
            await handle.Lease.HeartbeatAsync(cancellationToken).ConfigureAwait(false);
            handle.RefreshHeartbeat(_timeProvider.GetUtcNow());
            return Result.Ok(CreateContractLease(handle));
        }
        catch (InvalidOperationException)
        {
            _leases.TryRemove(leaseId, out _);
            return Result.Fail<TaskLease>(
                Error.From($"Lease {leaseId} is no longer active.", OdinErrorCodes.TaskLeaseExpired));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result.Fail<TaskLease>(Error.Canceled(token: cancellationToken));
        }
        catch (Exception ex)
        {
            return Result.Fail<TaskLease>(
                Error.From($"Heartbeat failed: {ex.Message}", OdinErrorCodes.TaskQueueError));
        }
    }

    public async Task<Result<Unit>> CompleteAsync(
        Guid leaseId,
        CancellationToken cancellationToken = default)
    {
        if (!_leases.TryGetValue(leaseId, out var handle))
        {
            return Result.Fail<Unit>(
                Error.From($"Lease {leaseId} not found.", OdinErrorCodes.TaskLeaseExpired));
        }

        try
        {
            await handle.Lease.CompleteAsync(cancellationToken).ConfigureAwait(false);
            _leases.TryRemove(leaseId, out _);
            return Result.Ok(Unit.Value);
        }
        catch (InvalidOperationException)
        {
            _leases.TryRemove(leaseId, out _);
            return Result.Fail<Unit>(
                Error.From($"Lease {leaseId} has already been reclaimed.", OdinErrorCodes.TaskLeaseExpired));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result.Fail<Unit>(Error.Canceled(token: cancellationToken));
        }
        catch (Exception ex)
        {
            _leases.TryRemove(leaseId, out _);
            return Result.Fail<Unit>(
                Error.From($"Complete failed: {ex.Message}", OdinErrorCodes.TaskQueueError));
        }
    }

    public async Task<Result<Unit>> FailAsync(
        Guid leaseId,
        string reason,
        bool requeue = true,
        CancellationToken cancellationToken = default)
    {
        if (!_leases.TryGetValue(leaseId, out var handle))
        {
            return Result.Fail<Unit>(
                Error.From($"Lease {leaseId} not found.", OdinErrorCodes.TaskLeaseExpired));
        }

        try
        {
            var error = Error.From(reason, OdinErrorCodes.TaskQueueError);
            await handle.Lease.FailAsync(error, requeue, cancellationToken).ConfigureAwait(false);
            _leases.TryRemove(leaseId, out _);
            return Result.Ok(Unit.Value);
        }
        catch (InvalidOperationException)
        {
            _leases.TryRemove(leaseId, out _);
            return Result.Fail<Unit>(
                Error.From($"Lease {leaseId} has already been reclaimed.", OdinErrorCodes.TaskLeaseExpired));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result.Fail<Unit>(Error.Canceled(token: cancellationToken));
        }
        catch (Exception ex)
        {
            _leases.TryRemove(leaseId, out _);
            return Result.Fail<Unit>(
                Error.From($"Fail operation failed: {ex.Message}", OdinErrorCodes.TaskQueueError));
        }
    }

    public Task<Result<int>> GetQueueDepthAsync(
        string queueName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);

        if (!_queues.TryGetValue(queueName, out var context))
        {
            return Task.FromResult(Result.Ok(0));
        }

        var depth = context.Queue.PendingCount;
        return Task.FromResult(Result.Ok(depth > int.MaxValue ? int.MaxValue : (int)depth));
    }

    public Task<Result<Dictionary<string, int>>> ListQueuesAsync(
        string? namespaceId = null,
        CancellationToken cancellationToken = default)
    {
        var snapshot = _queues.ToArray();
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, context) in snapshot)
        {
            var depth = context.Queue.PendingCount;
            result[name] = depth > int.MaxValue ? int.MaxValue : (int)depth;
        }

        return Task.FromResult(Result.Ok(result));
    }

    public async Task<Result<int>> ReclaimExpiredLeasesAsync(
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var reclaimed = 0;

        foreach (var (leaseId, handle) in _leases.ToArray())
        {
            if (handle.LeaseExpiresAt > now)
            {
                continue;
            }

            if (!_leases.TryRemove(leaseId, out var removed))
            {
                continue;
            }

            try
            {
                var error = Error.From("Lease reclaimed due to expiration.", OdinErrorCodes.TaskLeaseExpired);
                await removed.Lease.FailAsync(error, requeue: true, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                // Lease already reclaimed by monitor; nothing else to do.
            }
            catch (Exception)
            {
                // Suppress secondary failures; reclamation still considered handled.
            }

            reclaimed++;
        }

        return Result.Ok(reclaimed);
    }

    public Task<Result<int>> PurgeOldTasksAsync(
        DateTimeOffset olderThan,
        CancellationToken cancellationToken = default)
    {
        // Hugo.TaskQueue does not expose direct random-access removal; defer to automatic retention policies.
        return Task.FromResult(Result.Ok(0));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var (_, context) in _queues)
        {
            await context.Adapter.DisposeAsync().ConfigureAwait(false);
            await context.Queue.DisposeAsync().ConfigureAwait(false);
        }

        _queues.Clear();
        _leases.Clear();
    }

    private QueueContext GetOrCreateQueue(string queueName)
    {
        return _queues.GetOrAdd(queueName, name =>
        {
            var options = _defaultOptions.ToTaskQueueOptions();
            var queue = new TaskQueue<QueueEntry>(options, _timeProvider);
            var adapter = TaskQueueChannelAdapter<QueueEntry>.Create(queue, concurrency: 1, ownsQueue: false);
            return new QueueContext(name, queue, adapter, _defaultOptions);
        });
    }

    private static TaskLease CreateContractLease(LeaseHandle handle)
    {
        var snapshot = handle.Snapshot;

        return new TaskLease
        {
            LeaseId = snapshot.LeaseId,
            Task = snapshot.Task,
            WorkerIdentity = snapshot.WorkerIdentity,
            LeasedAt = snapshot.LeasedAt,
            LeaseExpiresAt = snapshot.LeaseExpiresAt,
            HeartbeatAt = snapshot.HeartbeatAt,
            AttemptCount = snapshot.Attempt
        };
    }

    private sealed record QueueEntry(Guid InstanceId, TaskQueueItem Task);

    private sealed class QueueContext
    {
        public QueueContext(string name, TaskQueue<QueueEntry> queue, TaskQueueChannelAdapter<QueueEntry> adapter, OptionsSnapshot options)
        {
            Name = name;
            Queue = queue;
            Adapter = adapter;
            Options = options;
        }

        public string Name { get; }
        public TaskQueue<QueueEntry> Queue { get; }
        public TaskQueueChannelAdapter<QueueEntry> Adapter { get; }
        public OptionsSnapshot Options { get; }
        public ChannelReader<TaskQueueLease<QueueEntry>> Reader => Adapter.Reader;
    }

    private sealed class LeaseHandle
    {
        private readonly QueueContext _context;
        private readonly TaskQueueLease<QueueEntry> _lease;
        private readonly object _sync = new();
        private DateTimeOffset _heartbeatAt;
        private DateTimeOffset _expiresAt;

        public LeaseHandle(
            QueueContext context,
            string workerIdentity,
            Guid leaseId,
            TaskQueueLease<QueueEntry> lease,
            DateTimeOffset leasedAt)
        {
            _context = context;
            _lease = lease;
            WorkerIdentity = workerIdentity;
            LeaseId = leaseId;
            TaskId = lease.Value.InstanceId;
            LeasedAt = leasedAt;
            _heartbeatAt = leasedAt;
            _expiresAt = leasedAt + context.Options.LeaseDuration;
        }

        public Guid LeaseId { get; }
        public Guid TaskId { get; }
        public string WorkerIdentity { get; }
        public DateTimeOffset LeasedAt { get; }
        public TaskQueueLease<QueueEntry> Lease => _lease;

        public DateTimeOffset HeartbeatAt
        {
            get
            {
                lock (_sync)
                {
                    return _heartbeatAt;
                }
            }
        }

        public DateTimeOffset LeaseExpiresAt
        {
            get
            {
                lock (_sync)
                {
                    return _expiresAt;
                }
            }
        }

        public int Attempt => _lease.Attempt;

        public void RefreshHeartbeat(DateTimeOffset timestamp)
        {
            lock (_sync)
            {
                _heartbeatAt = timestamp;
                _expiresAt = timestamp + _context.Options.LeaseDuration;
            }
        }

        public LeaseSnapshot Snapshot
        {
            get
            {
                lock (_sync)
                {
                    return new LeaseSnapshot(
                        LeaseId,
                        TaskId,
                        WorkerIdentity,
                        LeasedAt,
                        _heartbeatAt,
                        _expiresAt,
                        _lease.Attempt,
                        _lease.Value.Task);
                }
            }
        }
    }

    private readonly record struct LeaseSnapshot(
        Guid LeaseId,
        Guid TaskId,
        string WorkerIdentity,
        DateTimeOffset LeasedAt,
        DateTimeOffset HeartbeatAt,
        DateTimeOffset LeaseExpiresAt,
        int Attempt,
        TaskQueueItem Task);

    private readonly record struct OptionsSnapshot(
        int Capacity,
        TimeSpan LeaseDuration,
        TimeSpan HeartbeatInterval,
        TimeSpan LeaseSweepInterval,
        TimeSpan RequeueDelay,
        int MaxDeliveryAttempts)
    {
        public OptionsSnapshot(TaskQueueOptions options)
            : this(
                options.Capacity,
                options.LeaseDuration,
                options.HeartbeatInterval,
                options.LeaseSweepInterval,
                options.RequeueDelay,
                options.MaxDeliveryAttempts)
        {
        }

        public TaskQueueOptions ToTaskQueueOptions() => new()
        {
            Capacity = Capacity,
            LeaseDuration = LeaseDuration,
            HeartbeatInterval = HeartbeatInterval,
            LeaseSweepInterval = LeaseSweepInterval,
            RequeueDelay = RequeueDelay,
            MaxDeliveryAttempts = MaxDeliveryAttempts
        };
    }
}
