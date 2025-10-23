using System.Text.Json;
using System.Threading;
using Hugo;
using Microsoft.Extensions.Logging;
using Odin.Contracts;
using Odin.Core;
using Odin.Persistence.Interfaces;
using static Hugo.Go;
using static Hugo.Functional;

namespace Odin.ExecutionEngine.Matching;

/// <summary>
/// Matching service manages task queue operations and worker polling.
/// Implements partition-aware task distribution with lease-based delivery.
/// </summary>
public sealed class MatchingService(
    ITaskQueueRepository taskQueueRepository,
    ILogger<MatchingService> logger,
    JsonSerializerOptions? serializerOptions = null) : IMatchingService
{
    private readonly ITaskQueueRepository _taskQueueRepository = taskQueueRepository;
    private readonly ILogger<MatchingService> _logger = logger;
    private readonly JsonSerializerOptions _serializerOptions = serializerOptions ?? JsonOptions.Default;

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
                .OnFailure(error => _logger.LogError(
                    "Failed to enqueue task to queue {QueueName}: {Error}",
                    task.TaskQueueName,
                    error.Message))
                .OnSuccess(taskId => _logger.LogDebug(
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
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (leaseTimeout > TimeSpan.Zero)
            {
                linkedCts.CancelAfter(leaseTimeout);
            }

            return (await _taskQueueRepository.PollAsync(
                queueName,
                workerIdentity,
                linkedCts.Token).ConfigureAwait(false))
                .OnFailure(error => _logger.LogError(
                    "Failed to poll queue {QueueName}: {Error}",
                    queueName,
                    error.Message))
                .OnSuccess(lease =>
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
                cancellationToken).ConfigureAwait(false))
                .OnFailure(error => _logger.LogWarning(
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
        Guid leaseId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return (await _taskQueueRepository.CompleteAsync(
                leaseId,
                cancellationToken).ConfigureAwait(false))
                .OnFailure(error => _logger.LogWarning(
                    "Failed to complete lease {LeaseId}: {Error}",
                    leaseId,
                    error.Message))
                .OnSuccess(_ => _logger.LogDebug(
                    "Completed lease {LeaseId}",
                    leaseId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error completing lease {LeaseId}",
                leaseId);
            return Result.Fail<Unit>(
                Error.From($"Complete failed: {ex.Message}", OdinErrorCodes.TaskQueueError));
        }
    }

    /// <summary>
    /// Fails a task and optionally re-enqueues for retry.
    /// </summary>
    public async Task<Result<Unit>> FailTaskAsync(
        Guid leaseId,
        string reason,
        bool requeue = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return (await _taskQueueRepository.FailAsync(
                leaseId,
                reason,
                requeue,
                cancellationToken).ConfigureAwait(false))
                .OnFailure(error => _logger.LogWarning(
                    "Failed to mark lease {LeaseId} as failed: {Error}",
                    leaseId,
                    error.Message))
                .OnSuccess(_ => _logger.LogWarning(
                    "Lease {LeaseId} failed: {Reason} (requeue: {Requeue})",
                    leaseId,
                    reason,
                    requeue));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error failing lease {LeaseId}",
                leaseId);
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
                .OnFailure(error => _logger.LogError(
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
                .OnFailure(error => _logger.LogError(
                    "Failed to reclaim expired leases: {Error}",
                    error.Message))
                .OnSuccess(count =>
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

    public Task<MatchingSubscription> SubscribeAsync(
        string queueName,
        string workerIdentity,
        CancellationToken cancellationToken = default)
    {
        var dispatcher = new TaskQueueDispatcher(
            queueName,
            workerIdentity,
            _taskQueueRepository,
            _serializerOptions,
            _logger);

        if (cancellationToken.CanBeCanceled)
        {
#pragma warning disable CS4014
            cancellationToken.Register(state =>
            {
                var d = (TaskQueueDispatcher)state!;
                _ = d.DisposeAsync();
            }, dispatcher, useSynchronizationContext: false);
#pragma warning restore CS4014
        }

        return Task.FromResult(new MatchingSubscription(dispatcher));
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
        Guid leaseId,
        CancellationToken cancellationToken = default);

    Task<Result<Unit>> FailTaskAsync(
        Guid leaseId,
        string reason,
        bool requeue = true,
        CancellationToken cancellationToken = default);

    Task<Result<QueueStats>> GetQueueStatsAsync(
        string queueName,
        CancellationToken cancellationToken = default);

    Task<Result<int>> ReclaimExpiredLeasesAsync(
        CancellationToken cancellationToken = default);

    Task<MatchingSubscription> SubscribeAsync(
        string queueName,
        string workerIdentity,
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
