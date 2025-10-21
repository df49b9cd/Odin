using Hugo;
using Microsoft.AspNetCore.Mvc;
using Odin.Contracts;
using Odin.ExecutionEngine.Matching;
using static Hugo.Go;
using QueueStats = Odin.ExecutionEngine.Matching.QueueStats;

namespace Odin.ControlPlane.Api.Controllers;

/// <summary>
/// Task queue endpoints for worker polling and task lifecycle.
/// </summary>
[ApiController]
[Route("api/v1/tasks")]
[Produces("application/json")]
public sealed class TaskQueueController : ControllerBase
{
    private readonly IMatchingService _matchingService;
    private readonly ILogger<TaskQueueController> _logger;

    public TaskQueueController(
        IMatchingService matchingService,
        ILogger<TaskQueueController> logger)
    {
        _matchingService = matchingService;
        _logger = logger;
    }

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
        if (string.IsNullOrWhiteSpace(request.TaskQueue))
        {
            return BadRequest(new ErrorResponse
            {
                Message = "Task queue name is required",
                Code = "INVALID_REQUEST"
            });
        }

        if (string.IsNullOrWhiteSpace(request.WorkerIdentity))
        {
            return BadRequest(new ErrorResponse
            {
                Message = "Worker identity is required",
                Code = "INVALID_REQUEST"
            });
        }

        var result = await _matchingService.PollTaskAsync(
            request.TaskQueue,
            request.WorkerIdentity,
            TimeSpan.FromSeconds(request.TaskTimeoutSeconds ?? 30),
            cancellationToken);

        if (result.IsFailure)
        {
            _logger.LogError("Failed to poll task from queue {TaskQueue}: {Error}",
                request.TaskQueue, result.Error?.Message);

            return BadRequest(new ErrorResponse
            {
                Message = result.Error?.Message ?? "Poll failed",
                Code = result.Error?.Code ?? "POLL_FAILED"
            });
        }

        // No task available
        if (result.Value == null)
        {
            return NoContent();
        }

        var taskLease = result.Value;

        _logger.LogDebug("Worker {WorkerIdentity} received task from queue {TaskQueue}",
            request.WorkerIdentity, request.TaskQueue);

        return Ok(new PollTaskResponse
        {
            TaskId = taskLease.Task.TaskId.ToString(),
            WorkflowId = taskLease.Task.WorkflowId,
            RunId = taskLease.Task.RunId.ToString(),
            ScheduledEventId = taskLease.Task.TaskId,
            LeasedUntil = taskLease.LeaseExpiresAt.DateTime
        });
    }

    /// <summary>
    /// Send a heartbeat to extend task lease.
    /// </summary>
    /// <param name="taskId">Task ID</param>
    /// <param name="request">Heartbeat request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Success or error</returns>
    [HttpPost("{taskId}/heartbeat")]
    [ProducesResponseType(typeof(HeartbeatTaskResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> HeartbeatTask(
        [FromRoute] string taskId,
        [FromBody] HeartbeatTaskRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.WorkerIdentity))
        {
            return BadRequest(new ErrorResponse
            {
                Message = "Worker identity is required",
                Code = "INVALID_REQUEST"
            });
        }

        // Parse taskId as Guid (assuming it's the lease ID)
        if (!Guid.TryParse(taskId, out var leaseId))
        {
            return BadRequest(new ErrorResponse
            {
                Message = "Invalid task ID format",
                Code = "INVALID_REQUEST"
            });
        }

        var result = await _matchingService.HeartbeatTaskAsync(
            leaseId,
            TimeSpan.FromSeconds(request.ExtensionSeconds ?? 30),
            cancellationToken);

        if (result.IsFailure)
        {
            _logger.LogWarning("Failed to heartbeat task {TaskId}: {Error}",
                taskId, result.Error?.Message);

            return NotFound(new ErrorResponse
            {
                Message = result.Error?.Message ?? "Heartbeat failed",
                Code = result.Error?.Code ?? "HEARTBEAT_FAILED"
            });
        }

        return Ok(new HeartbeatTaskResponse
        {
            Success = true,
            LeasedUntil = result.Value.LeaseExpiresAt.DateTime
        });
    }

    /// <summary>
    /// Complete a task successfully.
    /// </summary>
    /// <param name="taskId">Task ID</param>
    /// <param name="request">Completion request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Success or error</returns>
    [HttpPost("{taskId}/complete")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CompleteTask(
        [FromRoute] string taskId,
        [FromBody] CompleteTaskRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.WorkerIdentity))
        {
            return BadRequest(new ErrorResponse
            {
                Message = "Worker identity is required",
                Code = "INVALID_REQUEST"
            });
        }

        // Parse taskId as Guid  
        if (!Guid.TryParse(taskId, out var taskGuid))
        {
            return BadRequest(new ErrorResponse
            {
                Message = "Invalid task ID format",
                Code = "INVALID_REQUEST"
            });
        }

        // For Phase 1, use taskId as both task and lease ID
        var result = await _matchingService.CompleteTaskAsync(
            taskGuid,
            taskGuid,
            cancellationToken);

        if (result.IsFailure)
        {
            _logger.LogError("Failed to complete task {TaskId}: {Error}",
                taskId, result.Error?.Message);

            return NotFound(new ErrorResponse
            {
                Message = result.Error?.Message ?? "Complete failed",
                Code = result.Error?.Code ?? "COMPLETE_FAILED"
            });
        }

        _logger.LogInformation("Task {TaskId} completed by worker {WorkerIdentity}",
            taskId, request.WorkerIdentity);

        return Accepted();
    }

    /// <summary>
    /// Fail a task with optional retry.
    /// </summary>
    /// <param name="taskId">Task ID</param>
    /// <param name="request">Failure request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Success or error</returns>
    [HttpPost("{taskId}/fail")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> FailTask(
        [FromRoute] string taskId,
        [FromBody] FailTaskRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.WorkerIdentity))
        {
            return BadRequest(new ErrorResponse
            {
                Message = "Worker identity is required",
                Code = "INVALID_REQUEST"
            });
        }

        // Parse taskId as Guid
        if (!Guid.TryParse(taskId, out var taskGuid))
        {
            return BadRequest(new ErrorResponse
            {
                Message = "Invalid task ID format",
                Code = "INVALID_REQUEST"
            });
        }

        // For Phase 1, use taskId as both task and lease ID
        var result = await _matchingService.FailTaskAsync(
            taskGuid,
            taskGuid,
            request.Error ?? "Task failed",
            request.Requeue ?? false,
            cancellationToken);

        if (result.IsFailure)
        {
            _logger.LogError("Failed to mark task {TaskId} as failed: {Error}",
                taskId, result.Error?.Message);

            return NotFound(new ErrorResponse
            {
                Message = result.Error?.Message ?? "Fail operation failed",
                Code = result.Error?.Code ?? "FAIL_FAILED"
            });
        }

        _logger.LogWarning("Task {TaskId} failed by worker {WorkerIdentity}: {Error} (requeue={Requeue})",
            taskId, request.WorkerIdentity, request.Error, request.Requeue);

        return Accepted();
    }

    /// <summary>
    /// Get task queue statistics.
    /// </summary>
    /// <param name="queueName">Task queue name</param>
    /// <param name="namespaceId">Namespace ID (default: "default")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Queue statistics</returns>
    [HttpGet("queues/{queueName}/stats")]
    [ProducesResponseType(typeof(QueueStats), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetQueueStats(
        [FromRoute] string queueName,
        CancellationToken cancellationToken = default)
    {
        var result = await _matchingService.GetQueueStatsAsync(queueName, cancellationToken);

        if (result.IsFailure)
        {
            _logger.LogError("Failed to get stats for queue {QueueName}: {Error}",
                queueName, result.Error?.Message);

            return BadRequest(new ErrorResponse
            {
                Message = result.Error?.Message ?? "Failed to get queue stats",
                Code = result.Error?.Code ?? "STATS_FAILED"
            });
        }

        return Ok(result.Value);
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
    public required string TaskId { get; init; }
    public required string WorkflowId { get; init; }
    public required string RunId { get; init; }
    public required long ScheduledEventId { get; init; }
    public DateTime? LeasedUntil { get; init; }
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
