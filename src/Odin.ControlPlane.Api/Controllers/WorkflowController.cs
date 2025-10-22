using System.Collections.Generic;
using System.Text.Json;
using Hugo;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.ExecutionEngine.History;
using Odin.Persistence.Interfaces;
using static Hugo.Functional;
using static Hugo.Go;
using WorkflowExecutionModel = Odin.Contracts.WorkflowExecution;

namespace Odin.ControlPlane.Api.Controllers;

/// <summary>
/// Workflow lifecycle management endpoints.
/// </summary>
[ApiController]
[Route("api/v1/workflows")]
[Produces("application/json")]
public sealed class WorkflowController(
    IWorkflowExecutionRepository workflowRepository,
    IHistoryService historyService,
    ITaskQueueRepository taskQueueRepository,
    ILogger<WorkflowController> logger) : ControllerBase
{
    private readonly IWorkflowExecutionRepository _workflowRepository = workflowRepository;
    private readonly IHistoryService _historyService = historyService;
    private readonly ITaskQueueRepository _taskQueueRepository = taskQueueRepository;
    private readonly ILogger<WorkflowController> _logger = logger;

    /// <summary>
    /// Start a new workflow execution.
    /// </summary>
    [HttpPost("start")]
    [ProducesResponseType(typeof(StartWorkflowResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> StartWorkflow(
        [FromBody] StartWorkflowRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.WorkflowType))
        {
            return BadRequest(new ErrorResponse
            {
                Message = "Workflow type is required",
                Code = "INVALID_REQUEST"
            });
        }

        // Generate IDs
        var workflowId = request.WorkflowId ?? Guid.NewGuid().ToString();
        var runId = Guid.NewGuid();

        // Create workflow execution record
        var execution = new WorkflowExecutionModel
        {
            NamespaceId = Guid.Parse(request.NamespaceId),
            WorkflowId = workflowId,
            RunId = runId,
            WorkflowType = request.WorkflowType,
            TaskQueue = request.TaskQueue,
            WorkflowState = Odin.Contracts.WorkflowState.Running,
            StartedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow,
            ShardId = 0 // Will be calculated by repository
        };

        var createResult = await _workflowRepository.CreateAsync(execution, cancellationToken);
        if (createResult.IsFailure)
        {
            _logger.LogError("Failed to create workflow {WorkflowId}: {Error}",
                workflowId, createResult.Error?.Message);

            return BadRequest(new ErrorResponse
            {
                Message = createResult.Error?.Message ?? "Failed to start workflow",
                Code = createResult.Error?.Code ?? "CREATE_FAILED"
            });
        }

        // Append WorkflowExecutionStarted event
        // TODO: Phase 2 - Implement proper history event appending
        /*
        var startedEvent = new Odin.Contracts.HistoryEvent
        {
            EventId = 1,
            EventType = "WorkflowExecutionStarted",
            EventTimestamp = DateTimeOffset.UtcNow,
            EventData = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                WorkflowType = request.WorkflowType,
                TaskQueue = request.TaskQueue,
                Input = request.Input ?? ""
            }))
        };

        var appendRequest = new AppendHistoryEventsRequest
        {
            NamespaceId = request.NamespaceId,
            WorkflowId = workflowId,
            RunId = runId.ToString(),
            Events = new[] { startedEvent }
        };

        var appendResult = await _historyService.AppendEventsAsync(appendRequest, cancellationToken);
        if (appendResult.IsFailure)
        {
            _logger.LogWarning("Failed to append start event for workflow {WorkflowId}: {Error}",
                workflowId, appendResult.Error?.Message);
        }
        */

        // Enqueue workflow task
        // TODO: Phase 2 - Implement proper task queuing
        /*
        var workflowTask = new Odin.Contracts.WorkflowTask
        {
            TaskId = Guid.NewGuid().ToString(),
            NamespaceId = request.NamespaceId,
            WorkflowId = workflowId,
            RunId = runId,
            TaskQueue = request.TaskQueue,
            ScheduledEventId = 1,
            CreatedAt = DateTime.UtcNow
        };

        var enqueueResult = await _taskQueueRepository.EnqueueAsync(workflowTask, cancellationToken);
        if (enqueueResult.IsFailure)
        {
            _logger.LogWarning("Failed to enqueue task for workflow {WorkflowId}: {Error}",
                workflowId, enqueueResult.Error?.Message);
        }
        */

        _logger.LogInformation("Started workflow {WorkflowType} with ID {WorkflowId}/{RunId}",
            request.WorkflowType, workflowId, runId);

        return CreatedAtAction(
            nameof(GetWorkflow),
            new { id = workflowId },
            new StartWorkflowResponse
            {
                WorkflowId = workflowId,
                RunId = runId.ToString()
            });
    }

    /// <summary>
    /// Get workflow execution details.
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(WorkflowExecutionModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetWorkflow(
        [FromRoute] string id,
        [FromQuery] string? namespaceId = null,
        [FromQuery] string? runId = null,
        CancellationToken cancellationToken = default)
    {
        namespaceId ??= "default";

        var result = string.IsNullOrEmpty(runId)
            ? await _workflowRepository.GetCurrentAsync(namespaceId, id, cancellationToken)
            : await _workflowRepository.GetAsync(namespaceId, id, runId, cancellationToken);

        return Functional.Finally(result,
            workflow => (IActionResult)Ok(workflow),
            error => NotFound(new ErrorResponse
            {
                Message = error.Message ?? $"Workflow '{id}' not found",
                Code = error.Code ?? OdinErrorCodes.WorkflowNotFound
            }));
    }

    /// <summary>
    /// Signal a running workflow.
    /// </summary>
    [HttpPost("{id}/signal")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SignalWorkflow(
        [FromRoute] string id,
        [FromBody] SignalWorkflowRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SignalName))
        {
            return BadRequest(new ErrorResponse
            {
                Message = "Signal name is required",
                Code = "INVALID_REQUEST"
            });
        }

        var namespaceId = request.NamespaceId ?? "default";

        var fetchedWorkflow = await _workflowRepository.GetCurrentAsync(namespaceId, id, cancellationToken);
        var workflowResult = fetchedWorkflow.Ensure(
            workflow => workflow.WorkflowState == Odin.Contracts.WorkflowState.Running,
            workflow =>
            {
                var metadata = new Dictionary<string, object?>
                {
                    ["workflowId"] = id,
                    ["namespaceId"] = namespaceId,
                    ["currentState"] = workflow.WorkflowState.ToString()
                };

                return Error.From(
                        $"Workflow is not running (current status: {workflow.WorkflowState})",
                        "INVALID_WORKFLOW_STATE")
                    .WithMetadata(metadata);
            });

        // Append WorkflowExecutionSignaled event
        // TODO: Phase 2 - Implement signal event append
        /*
        var signalEvent = new Odin.Contracts.HistoryEvent
        {
            EventId = 0, // Will be assigned by repository
            EventType = "WorkflowExecutionSignaled",
            Timestamp = DateTime.UtcNow,
            Attributes = new Dictionary<string, object>
            {
                ["SignalName"] = request.SignalName,
                ["Input"] = request.Input ?? ""
            }
        };

        // Note: In production, need to get next event ID from history
        // For now, using placeholder approach
        */
        _logger.LogInformation("Signaled workflow {WorkflowId} with signal {SignalName}",
            id, request.SignalName);

        return Functional.Finally(workflowResult,
            _ =>
            {
                _logger.LogInformation("Signal {SignalName} accepted for workflow {WorkflowId}", request.SignalName, id);
                return (IActionResult)Accepted();
            },
            error => (error.Code ?? string.Empty) switch
            {
                OdinErrorCodes.WorkflowNotFound => NotFound(new ErrorResponse
                {
                    Message = error.Message ?? $"Workflow '{id}' not found",
                    Code = OdinErrorCodes.WorkflowNotFound
                }),
                "INVALID_WORKFLOW_STATE" => BadRequest(new ErrorResponse
                {
                    Message = error.Message ?? "Workflow is not in a running state",
                    Code = "INVALID_WORKFLOW_STATE"
                }),
                _ => BadRequest(new ErrorResponse
                {
                    Message = error.Message ?? "Signal request failed",
                    Code = error.Code ?? "SIGNAL_FAILED"
                })
            });
    }

    /// <summary>
    /// Terminate a running workflow.
    /// </summary>
    [HttpPost("{id}/terminate")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> TerminateWorkflow(
        [FromRoute] string id,
        [FromBody] TerminateWorkflowRequest request,
        CancellationToken cancellationToken)
    {
        var namespaceId = request.NamespaceId ?? "default";

        // Get current workflow execution
        var getResult = await _workflowRepository.GetCurrentAsync(namespaceId, id, cancellationToken);
        if (getResult.IsFailure)
        {
            return NotFound(new ErrorResponse
            {
                Message = $"Workflow '{id}' not found",
                Code = "WORKFLOW_NOT_FOUND"
            });
        }

        var workflow = getResult.Value;
        if (workflow.WorkflowState != Odin.Contracts.WorkflowState.Running)
        {
            return BadRequest(new ErrorResponse
            {
                Message = $"Workflow is not running (current status: {workflow.WorkflowState})",
                Code = "INVALID_WORKFLOW_STATE"
            });
        }

        // Terminate workflow
        var updateResult = await _workflowRepository.TerminateAsync(
            namespaceId,
            id,
            workflow.RunId.ToString(),
            request.Reason ?? "Terminated by user",
            cancellationToken);

        if (updateResult.IsFailure)
        {
            _logger.LogError("Failed to terminate workflow {WorkflowId}: {Error}",
                id, updateResult.Error?.Message);

            return BadRequest(new ErrorResponse
            {
                Message = updateResult.Error?.Message ?? "Failed to terminate workflow",
                Code = updateResult.Error?.Code ?? "TERMINATE_FAILED"
            });
        }

        // Append WorkflowExecutionTerminated event
        // TODO: Phase 2 - Implement termination event
        /*
        var terminatedEvent = new Odin.Contracts.HistoryEvent
        {
            EventId = 0, // Placeholder
            EventType = "WorkflowExecutionTerminated",
            Timestamp = DateTime.UtcNow,
            Attributes = new Dictionary<string, object>
            {
                ["Reason"] = request.Reason ?? "Terminated by user"
            }
        };
        */

        _logger.LogInformation("Terminated workflow {WorkflowId}/{RunId}: {Reason}",
            id, workflow.RunId.ToString(), request.Reason);

        return Accepted();
    }

    /// <summary>
    /// Query a workflow execution (read-only operation).
    /// </summary>
    [HttpPost("{id}/query")]
    [ProducesResponseType(typeof(QueryWorkflowResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> QueryWorkflow(
        [FromRoute] string id,
        [FromBody] QueryWorkflowRequest request,
        CancellationToken cancellationToken)
    {
        var namespaceId = request.NamespaceId ?? "default";

        // Get current workflow execution
        var getResult = await _workflowRepository.GetCurrentAsync(namespaceId, id, cancellationToken);
        if (getResult.IsFailure)
        {
            return NotFound(new ErrorResponse
            {
                Message = $"Workflow '{id}' not found",
                Code = "WORKFLOW_NOT_FOUND"
            });
        }

        _logger.LogInformation("Query {QueryType} on workflow {WorkflowId}",
            request.QueryType, id);

        // In production, this would execute query handler against workflow state
        // For Phase 1, return basic workflow info
        return Ok(new QueryWorkflowResponse
        {
            Result = new Dictionary<string, object>
            {
                ["status"] = getResult.Value.WorkflowState.ToString(),
                ["workflowType"] = getResult.Value.WorkflowType,
                ["startTime"] = getResult.Value.StartedAt
            }
        });
    }
}

/// <summary>
/// Request to start a workflow.
/// </summary>
public sealed record StartWorkflowRequest
{
    public string NamespaceId { get; init; } = "default";
    public string? WorkflowId { get; init; }
    public required string WorkflowType { get; init; }
    public required string TaskQueue { get; init; }
    public string? Input { get; init; }
}

/// <summary>
/// Response from starting a workflow.
/// </summary>
public sealed record StartWorkflowResponse
{
    public required string WorkflowId { get; init; }
    public required string RunId { get; init; }
}

/// <summary>
/// Request to signal a workflow.
/// </summary>
public sealed record SignalWorkflowRequest
{
    public string? NamespaceId { get; init; }
    public required string SignalName { get; init; }
    public string? Input { get; init; }
}

/// <summary>
/// Request to terminate a workflow.
/// </summary>
public sealed record TerminateWorkflowRequest
{
    public string? NamespaceId { get; init; }
    public string? Reason { get; init; }
}

/// <summary>
/// Request to query a workflow.
/// </summary>
public sealed record QueryWorkflowRequest
{
    public string? NamespaceId { get; init; }
    public required string QueryType { get; init; }
    public Dictionary<string, object>? Args { get; init; }
}

/// <summary>
/// Response from querying a workflow.
/// </summary>
public sealed record QueryWorkflowResponse
{
    public required Dictionary<string, object> Result { get; init; }
}
