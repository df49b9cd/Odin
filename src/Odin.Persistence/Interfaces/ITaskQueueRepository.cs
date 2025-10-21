using Hugo;
using Odin.Contracts;
using static Hugo.Go;

namespace Odin.Persistence.Interfaces;

/// <summary>
/// Repository for task queue management and lease operations.
/// Implements task distribution with lease-based worker assignment.
/// </summary>
public interface ITaskQueueRepository
{
    /// <summary>
    /// Enqueues a new task to a specified task queue.
    /// </summary>
    Task<Result<Guid>> EnqueueAsync(
        TaskQueueItem task,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Polls for next available task with atomic lease acquisition.
    /// Returns null if no tasks available.
    /// </summary>
    Task<Result<TaskLease?>> PollAsync(
        string queueName,
        string workerIdentity,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Renews (heartbeats) an existing task lease.
    /// </summary>
    Task<Result<TaskLease>> HeartbeatAsync(
        Guid leaseId,
        TimeSpan extendBy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes a task and removes it from the queue.
    /// </summary>
    Task<Result<Unit>> CompleteAsync(
        Guid taskId,
        Guid leaseId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fails a task and optionally re-enqueues for retry.
    /// </summary>
    Task<Result<Unit>> FailAsync(
        Guid taskId,
        Guid leaseId,
        string reason,
        bool requeue = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the depth (pending task count) of a task queue.
    /// </summary>
    Task<Result<int>> GetQueueDepthAsync(
        string queueName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all task queues with their depths.
    /// </summary>
    Task<Result<Dictionary<string, int>>> ListQueuesAsync(
        string? namespaceId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reclaims expired leases and makes tasks available again.
    /// Should be called periodically by maintenance workers.
    /// </summary>
    Task<Result<int>> ReclaimExpiredLeasesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Purges completed or expired tasks older than retention period.
    /// </summary>
    Task<Result<int>> PurgeOldTasksAsync(
        DateTimeOffset olderThan,
        CancellationToken cancellationToken = default);
}
