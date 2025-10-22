using Hugo;
using Microsoft.Extensions.Logging;
using Odin.Contracts;
using Odin.Core;
using Odin.Persistence.Interfaces;
using static Hugo.Go;

namespace Odin.ExecutionEngine.Matching;

/// <summary>
/// Matching service manages task queue operations and worker polling.
/// Implements partition-aware task distribution with lease-based delivery.
/// </summary>
public sealed class MatchingService : IMatchingService
{
    private readonly ITaskQueueRepository _taskQueueRepository;
    private readonly ILogger<MatchingService> _logger;

    public MatchingService(
        ITaskQueueRepository taskQueueRepository,
        ILogger<MatchingService> logger)
    {
        _taskQueueRepository = taskQueueRepository;
        _logger = logger;
    }

    /// <summary>
    /// Enqueues a workflow or activity task.
    /// </summary>
    public async Task<Result<Guid>> EnqueueTaskAsync(
        TaskQueueItem task,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return (await _taskQueueRepository.EnqueueAsync(task, cancellationToken).ConfigureAwait(false))
                .TapError(error => _logger.LogError(
                    "Failed to enqueue task to queue {QueueName}: {Error}",
                    task.TaskQueueName,
                    error.Message))
                .Tap(taskId => _logger.LogDebug(
                    "Enqueued task {TaskId} to queue {QueueName} (type: {TaskType})",
                    taskId,
                    task.TaskQueueName,
                    task.TaskQueueType));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error enqueuing task to queue {QueueName}",
                task.TaskQueueName);
            return Result.Fail<Guid>(
                Error.From($"Enqueue failed: {ex.Message}", OdinErrorCodes.TaskQueueError));
        }
    }

    /// <summary>
    /// Polls for next available task with lease acquisition.
    /// Returns null if no tasks available.
    /// </summary>
    public async Task<Result<TaskLease?>> PollTaskAsync(
        string queueName,
        string workerIdentity,
        TimeSpan leaseTimeout,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return (await _taskQueueRepository.PollAsync(
                queueName,
                workerIdentity,
                leaseTimeout,
                cancellationToken).ConfigureAwait(false))
                .TapError(error => _logger.LogError(
                    "Failed to poll queue {QueueName}: {Error}",
                    queueName,
                    error.Message))
                .Tap(lease =>
                {
                    if (lease is not null)
                    {
                        _logger.LogDebug(
                            "Worker {WorkerIdentity} acquired task from queue {QueueName} (lease: {LeaseId})",
                            workerIdentity,
                            queueName,
                            lease.LeaseId);
                    }
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error polling queue {QueueName}",
                queueName);
            return Result.Fail<TaskLease?>(
                Error.From($"Poll failed: {ex.Message}", OdinErrorCodes.TaskQueueError));
        }
    }

    /// <summary>
    /// Renews task lease (heartbeat).
    /// </summary>
    public async Task<Result<TaskLease>> HeartbeatTaskAsync(
        Guid leaseId,
        TimeSpan extendBy,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return (await _taskQueueRepository.HeartbeatAsync(
                leaseId,
                extendBy,
                cancellationToken).ConfigureAwait(false))
                .TapError(error => _logger.LogWarning(
                    "Failed to heartbeat lease {LeaseId}: {Error}",
                    leaseId,
                    error.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error heartbeating lease {LeaseId}",
                leaseId);
            return Result.Fail<TaskLease>(
                Error.From($"Heartbeat failed: {ex.Message}", OdinErrorCodes.TaskQueueError));
        }
    }

    /// <summary>
    /// Completes a task and removes it from the queue.
    /// </summary>
    public async Task<Result<Unit>> CompleteTaskAsync(
        Guid taskId,
        Guid leaseId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return (await _taskQueueRepository.CompleteAsync(
                taskId,
                leaseId,
                cancellationToken).ConfigureAwait(false))
                .TapError(error => _logger.LogWarning(
                    "Failed to complete task {TaskId} with lease {LeaseId}: {Error}",
                    taskId,
                    leaseId,
                    error.Message))
                .Tap(_ => _logger.LogDebug(
                    "Completed task {TaskId} with lease {LeaseId}",
                    taskId,
                    leaseId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error completing task {TaskId}",
                taskId);
            return Result.Fail<Unit>(
                Error.From($"Complete failed: {ex.Message}", OdinErrorCodes.TaskQueueError));
        }
    }

    /// <summary>
    /// Fails a task and optionally re-enqueues for retry.
    /// </summary>
    public async Task<Result<Unit>> FailTaskAsync(
        Guid taskId,
        Guid leaseId,
        string reason,
        bool requeue = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return (await _taskQueueRepository.FailAsync(
                taskId,
                leaseId,
                reason,
                requeue,
                cancellationToken).ConfigureAwait(false))
                .TapError(error => _logger.LogWarning(
                    "Failed to mark task {TaskId} as failed: {Error}",
                    taskId,
                    error.Message))
                .Tap(_ => _logger.LogWarning(
                    "Task {TaskId} failed: {Reason} (requeue: {Requeue})",
                    taskId,
                    reason,
                    requeue));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error failing task {TaskId}",
                taskId);
            return Result.Fail<Unit>(
                Error.From($"Fail task error: {ex.Message}", OdinErrorCodes.TaskQueueError));
        }
    }

    /// <summary>
    /// Gets queue statistics.
    /// </summary>
    public async Task<Result<QueueStats>> GetQueueStatsAsync(
        string queueName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return (await _taskQueueRepository.GetQueueDepthAsync(
                queueName,
                cancellationToken).ConfigureAwait(false))
                .TapError(error => _logger.LogError(
                    "Failed to get stats for queue {QueueName}: {Error}",
                    queueName,
                    error.Message))
                .Map(depth => new QueueStats
                {
                    QueueName = queueName,
                    PendingTasks = depth
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to get stats for queue {QueueName}",
                queueName);
            return Result.Fail<QueueStats>(
                Error.From($"Get stats failed: {ex.Message}", OdinErrorCodes.TaskQueueError));
        }
    }

    /// <summary>
    /// Reclaims expired task leases (maintenance operation).
    /// </summary>
    public async Task<Result<int>> ReclaimExpiredLeasesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return (await _taskQueueRepository.ReclaimExpiredLeasesAsync(
                cancellationToken).ConfigureAwait(false))
                .TapError(error => _logger.LogError(
                    "Failed to reclaim expired leases: {Error}",
                    error.Message))
                .Tap(count =>
                {
                    if (count > 0)
                    {
                        _logger.LogInformation(
                            "Reclaimed {Count} expired task leases",
                            count);
                    }
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reclaim expired leases");
            return Result.Fail<int>(
                Error.From($"Reclaim failed: {ex.Message}", OdinErrorCodes.TaskQueueError));
        }
    }
}

/// <summary>
/// Matching service interface for dependency injection.
/// </summary>
public interface IMatchingService
{
    Task<Result<Guid>> EnqueueTaskAsync(
        TaskQueueItem task,
        CancellationToken cancellationToken = default);

    Task<Result<TaskLease?>> PollTaskAsync(
        string queueName,
        string workerIdentity,
        TimeSpan leaseTimeout,
        CancellationToken cancellationToken = default);

    Task<Result<TaskLease>> HeartbeatTaskAsync(
        Guid leaseId,
        TimeSpan extendBy,
        CancellationToken cancellationToken = default);

    Task<Result<Unit>> CompleteTaskAsync(
        Guid taskId,
        Guid leaseId,
        CancellationToken cancellationToken = default);

    Task<Result<Unit>> FailTaskAsync(
        Guid taskId,
        Guid leaseId,
        string reason,
        bool requeue = true,
        CancellationToken cancellationToken = default);

    Task<Result<QueueStats>> GetQueueStatsAsync(
        string queueName,
        CancellationToken cancellationToken = default);

    Task<Result<int>> ReclaimExpiredLeasesAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Queue statistics.
/// </summary>
public sealed record QueueStats
{
    public required string QueueName { get; init; }
    public required int PendingTasks { get; init; }
}
