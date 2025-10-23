using System;
using Hugo;
using Microsoft.AspNetCore.Mvc;
using Odin.Contracts;
using Odin.ExecutionEngine.Matching;
using static Hugo.Functional;
using static Hugo.Go;
using QueueStats = Odin.ExecutionEngine.Matching.QueueStats;

namespace Odin.ControlPlane.Api.Controllers;

/// <summary>
/// Task queue endpoints for worker polling and task lifecycle.
/// </summary>
[ApiController]
[Route("api/v1/tasks")]
[Produces("application/json")]
public sealed class TaskQueueController(
    IMatchingService matchingService,
    ILogger<TaskQueueController> logger) : ControllerBase
{
    private readonly IMatchingService _matchingService = matchingService;
    private readonly ILogger<TaskQueueController> _logger = logger;

    /// <summary>
    /// Poll for a task from the specified task queue (long poll).
    /// </summary>
    /// <param name="request">Poll request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task or null if none available</returns>
    [HttpPost("poll")]
    [ProducesResponseType(typeof(PollTaskResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PollTask(
        [FromBody] PollTaskRequest request,
        CancellationToken cancellationToken)
    {
        var pollPipeline = await Go.Ok(request)
            .Ensure(static r => !string.IsNullOrWhiteSpace(r.TaskQueue),
                static _ => Error.From("Task queue name is required", "INVALID_REQUEST"))
            .Ensure(static r => !string.IsNullOrWhiteSpace(r.WorkerIdentity),
                static _ => Error.From("Worker identity is required", "INVALID_REQUEST"))
            .Ensure(static r => !r.TaskTimeoutSeconds.HasValue || r.TaskTimeoutSeconds.Value > 0,
                static r => Error.From("Task timeout must be positive", "INVALID_REQUEST"))
            .ThenAsync((validated, ct) => _matchingService.PollTaskAsync(
                validated.TaskQueue,
                validated.WorkerIdentity,
                TimeSpan.FromSeconds(validated.TaskTimeoutSeconds ?? 30),
                ct), cancellationToken)
            .ConfigureAwait(false);

        var pollResult = pollPipeline
            .OnFailure(error => _logger.LogError(
                "Failed to poll task from queue {TaskQueue}: {Error}",
                request.TaskQueue,
                error.Message))
            .OnSuccess(taskLease =>
            {
                if (taskLease is not null)
                {
                    _logger.LogDebug(
                        "Worker {WorkerIdentity} received task from queue {TaskQueue}",
                        request.WorkerIdentity,
                        request.TaskQueue);
                }
            });

        return pollResult.Match<IActionResult>(
            success => success is null
                ? NoContent()
                : Ok(new PollTaskResponse
                {
                    LeaseId = success.LeaseId,
                    TaskId = success.LeaseId.ToString(),
                    WorkflowId = success.Task.WorkflowId,
                    RunId = success.Task.RunId.ToString(),
                    ScheduledEventId = success.Task.TaskId,
                    LeasedUntil = success.LeaseExpiresAt.UtcDateTime,
                    Attempt = success.AttemptCount
                }),
            error => BadRequest(AsErrorResponse(error, "POLL_FAILED", "Poll failed")));
    }

    /// <summary>
    /// Send a heartbeat to extend task lease.
    /// </summary>
    /// <param name="leaseId"></param>
    /// <param name="request">Heartbeat request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Success or error</returns>
    [HttpPost("{leaseId:guid}/heartbeat")]
    [ProducesResponseType(typeof(HeartbeatTaskResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> HeartbeatTask(
        [FromRoute] Guid leaseId,
        [FromBody] HeartbeatTaskRequest request,
        CancellationToken cancellationToken)
    {
        var heartbeatPipeline = await Go.Ok(request)
            .Ensure(static r => !string.IsNullOrWhiteSpace(r.WorkerIdentity),
                static _ => Error.From("Worker identity is required", "INVALID_REQUEST"))
            .Ensure(static r => !r.ExtensionSeconds.HasValue || r.ExtensionSeconds.Value > 0,
                static _ => Error.From("Extension seconds must be positive", "INVALID_REQUEST"))
            .ThenAsync((_, ct) => _matchingService.HeartbeatTaskAsync(
                leaseId,
                TimeSpan.FromSeconds(request.ExtensionSeconds ?? 30),
                ct), cancellationToken)
            .ConfigureAwait(false);

        var heartbeatResult = heartbeatPipeline
            .OnFailure(error => _logger.LogWarning(
                "Failed to heartbeat lease {LeaseId}: {Error}",
                leaseId,
                error.Message));

        return heartbeatResult.Match<IActionResult>(
            lease => Ok(new HeartbeatTaskResponse
            {
                Success = true,
                LeasedUntil = lease.LeaseExpiresAt.UtcDateTime
            }),
            error => string.Equals(error.Code, "INVALID_REQUEST", StringComparison.OrdinalIgnoreCase)
                ? BadRequest(AsErrorResponse(error, "INVALID_REQUEST", "Heartbeat failed"))
                : NotFound(AsErrorResponse(error, error.Code ?? "HEARTBEAT_FAILED", error.Message ?? "Heartbeat failed")));
    }

    /// <summary>
    /// Complete a task successfully.
    /// </summary>
    /// <param name="leaseId"></param>
    /// <param name="request">Completion request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Success or error</returns>
    [HttpPost("{leaseId:guid}/complete")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CompleteTask(
        [FromRoute] Guid leaseId,
        [FromBody] CompleteTaskRequest request,
        CancellationToken cancellationToken)
    {
        var completionPipeline = await Go.Ok(request)
            .Ensure(static r => !string.IsNullOrWhiteSpace(r.WorkerIdentity),
                static _ => Error.From("Worker identity is required", "INVALID_REQUEST"))
            .ThenAsync((_, ct) => _matchingService.CompleteTaskAsync(leaseId, ct), cancellationToken)
            .ConfigureAwait(false);

        var completionResult = completionPipeline
            .OnSuccess(_ => _logger.LogInformation(
                "Lease {LeaseId} completed by worker {WorkerIdentity}",
                leaseId,
                request.WorkerIdentity))
            .OnFailure(error => _logger.LogError(
                "Failed to complete lease {LeaseId}: {Error}",
                leaseId,
                error.Message));

        return completionResult.Match<IActionResult>(
            _ => Accepted(),
            error => string.Equals(error.Code, "INVALID_REQUEST", StringComparison.OrdinalIgnoreCase)
                ? BadRequest(AsErrorResponse(error, "INVALID_REQUEST", "Complete failed"))
                : NotFound(AsErrorResponse(error, error.Code ?? "COMPLETE_FAILED", error.Message ?? "Complete failed")));
    }

    /// <summary>
    /// Fail a task with optional retry.
    /// </summary>
    /// <param name="leaseId"></param>
    /// <param name="request">Failure request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Success or error</returns>
    [HttpPost("{leaseId:guid}/fail")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> FailTask(
        [FromRoute] Guid leaseId,
        [FromBody] FailTaskRequest request,
        CancellationToken cancellationToken)
    {
        var failurePipeline = await Go.Ok(request)
            .Ensure(static r => !string.IsNullOrWhiteSpace(r.WorkerIdentity),
                static _ => Error.From("Worker identity is required", "INVALID_REQUEST"))
            .Ensure(static r => string.IsNullOrWhiteSpace(r.Error) || r.Error!.Length <= 1024,
                static _ => Error.From("Error message too long", "INVALID_REQUEST"))
            .ThenAsync((payload, ct) => _matchingService.FailTaskAsync(
                leaseId,
                payload.Error ?? "Task failed",
                payload.Requeue ?? false,
                ct), cancellationToken)
            .ConfigureAwait(false);

        var failureResult = failurePipeline
            .OnSuccess(_ => _logger.LogWarning(
                "Lease {LeaseId} failed by worker {WorkerIdentity}: {Error} (requeue={Requeue})",
                leaseId,
                request.WorkerIdentity,
                request.Error,
                request.Requeue))
            .OnFailure(error => _logger.LogError(
                "Failed to mark lease {LeaseId} as failed: {Error}",
                leaseId,
                error.Message));

        return failureResult.Match<IActionResult>(
            _ => Accepted(),
            error => string.Equals(error.Code, "INVALID_REQUEST", StringComparison.OrdinalIgnoreCase)
                ? BadRequest(AsErrorResponse(error, "INVALID_REQUEST", "Fail operation failed"))
                : NotFound(AsErrorResponse(error, error.Code ?? "FAIL_FAILED", error.Message ?? "Fail operation failed")));
    }

    /// <summary>
    /// Get task queue statistics.
    /// </summary>
    /// <param name="queueName">Task queue name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Queue statistics</returns>
    [HttpGet("queues/{queueName}/stats")]
    [ProducesResponseType(typeof(QueueStats), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetQueueStats(
        [FromRoute] string queueName,
        CancellationToken cancellationToken = default)
    {
        var statsPipeline = await _matchingService.GetQueueStatsAsync(queueName, cancellationToken)
            .ConfigureAwait(false);

        var statsResult = statsPipeline
            .OnFailure(error => _logger.LogError(
                "Failed to get stats for queue {QueueName}: {Error}",
                queueName,
                error.Message));

        return statsResult.Match<IActionResult>(
            stats => Ok(stats),
            error => BadRequest(AsErrorResponse(error, "STATS_FAILED", "Failed to get queue stats")));
    }

    private static ErrorResponse AsErrorResponse(
        Error error,
        string fallbackCode,
        string fallbackMessage)
    {
        return new ErrorResponse
        {
            Message = string.IsNullOrWhiteSpace(error.Message)
                ? fallbackMessage
                : error.Message,
            Code = string.IsNullOrWhiteSpace(error.Code)
                ? fallbackCode
                : error.Code
        };
    }
}

/// <summary>
/// Request to poll for a task.
/// </summary>
public sealed record PollTaskRequest
{
    public string? NamespaceId { get; init; }
    public required string TaskQueue { get; init; }
    public required string WorkerIdentity { get; init; }
    public int? TaskTimeoutSeconds { get; init; }
}

/// <summary>
/// Response from polling for a task.
/// </summary>
public sealed record PollTaskResponse
{
    public required Guid LeaseId { get; init; }
    public required string TaskId { get; init; }
    public required string WorkflowId { get; init; }
    public required string RunId { get; init; }
    public required long ScheduledEventId { get; init; }
    public DateTime? LeasedUntil { get; init; }
    public int Attempt { get; init; }
}

/// <summary>
/// Request to heartbeat a task.
/// </summary>
public sealed record HeartbeatTaskRequest
{
    public required string WorkerIdentity { get; init; }
    public int? ExtensionSeconds { get; init; }
}

/// <summary>
/// Response from heartbeat.
/// </summary>
public sealed record HeartbeatTaskResponse
{
    public required bool Success { get; init; }
    public DateTime? LeasedUntil { get; init; }
}

/// <summary>
/// Request to complete a task.
/// </summary>
public sealed record CompleteTaskRequest
{
    public required string WorkerIdentity { get; init; }
    public string? Result { get; init; }
}

/// <summary>
/// Request to fail a task.
/// </summary>
public sealed record FailTaskRequest
{
    public required string WorkerIdentity { get; init; }
    public string? Error { get; init; }
    public bool? Requeue { get; init; }
}
