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
            var enqueueResult = await _taskQueueRepository.EnqueueAsync(task, cancellationToken);

            if (enqueueResult.IsFailure)
            {
                _logger.LogError(
                    "Failed to enqueue task to queue {QueueName}: {Error}",
                    task.TaskQueueName, enqueueResult.Error?.Message);
                return enqueueResult;
            }

            _logger.LogDebug(
                "Enqueued task {TaskId} to queue {QueueName} (type: {TaskType})",
                enqueueResult.Value, task.TaskQueueName, task.TaskQueueType);

            return enqueueResult;
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
            var pollResult = await _taskQueueRepository.PollAsync(
                queueName,
                workerIdentity,
                leaseTimeout,
                cancellationToken);

            if (pollResult.IsFailure)
            {
                _logger.LogError(
                    "Failed to poll queue {QueueName}: {Error}",
                    queueName, pollResult.Error?.Message);
                return pollResult;
            }

            if (pollResult.Value != null)
            {
                _logger.LogDebug(
                    "Worker {WorkerIdentity} acquired task from queue {QueueName} (lease: {LeaseId})",
                    workerIdentity, queueName, pollResult.Value.LeaseId);
            }

            return pollResult;
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
            var heartbeatResult = await _taskQueueRepository.HeartbeatAsync(
                leaseId,
                extendBy,
                cancellationToken);

            if (heartbeatResult.IsFailure)
            {
                _logger.LogWarning(
                    "Failed to heartbeat lease {LeaseId}: {Error}",
                    leaseId, heartbeatResult.Error?.Message);
                return heartbeatResult;
            }

            return heartbeatResult;
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
            var completeResult = await _taskQueueRepository.CompleteAsync(
                taskId,
                leaseId,
                cancellationToken);

            if (completeResult.IsFailure)
            {
                _logger.LogWarning(
                    "Failed to complete task {TaskId} with lease {LeaseId}: {Error}",
                    taskId, leaseId, completeResult.Error?.Message);
                return completeResult;
            }

            _logger.LogDebug(
                "Completed task {TaskId} with lease {LeaseId}",
                taskId, leaseId);

            return Result.Ok(Unit.Value);
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
            var failResult = await _taskQueueRepository.FailAsync(
                taskId,
                leaseId,
                reason,
                requeue,
                cancellationToken);

            if (failResult.IsFailure)
            {
                _logger.LogWarning(
                    "Failed to mark task {TaskId} as failed: {Error}",
                    taskId, failResult.Error?.Message);
                return failResult;
            }

            _logger.LogWarning(
                "Task {TaskId} failed: {Reason} (requeue: {Requeue})",
                taskId, reason, requeue);

            return Result.Ok(Unit.Value);
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
            var depthResult = await _taskQueueRepository.GetQueueDepthAsync(
                queueName,
                cancellationToken);

            if (depthResult.IsFailure)
            {
                return Result.Fail<QueueStats>(depthResult.Error!);
            }

            var stats = new QueueStats
            {
                QueueName = queueName,
                PendingTasks = depthResult.Value
            };

            return Result.Ok(stats);
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
            var reclaimResult = await _taskQueueRepository.ReclaimExpiredLeasesAsync(
                cancellationToken);

            if (reclaimResult.IsFailure)
            {
                _logger.LogError(
                    "Failed to reclaim expired leases: {Error}",
                    reclaimResult.Error?.Message);
                return reclaimResult;
            }

            if (reclaimResult.Value > 0)
            {
                _logger.LogInformation(
                    "Reclaimed {Count} expired task leases",
                    reclaimResult.Value);
            }

            return reclaimResult;
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
