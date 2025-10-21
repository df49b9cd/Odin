using Hugo;
using Microsoft.AspNetCore.Mvc;
using Odin.ExecutionEngine.History;
using static Hugo.Go;

namespace Odin.ControlPlane.Api.Controllers;

/// <summary>
/// Workflow history query endpoints.
/// </summary>
[ApiController]
[Route("api/v1/workflows/{workflowId}/history")]
[Produces("application/json")]
public sealed class HistoryController : ControllerBase
{
    private readonly IHistoryService _historyService;
    private readonly ILogger<HistoryController> _logger;

    public HistoryController(
        IHistoryService historyService,
        ILogger<HistoryController> logger)
    {
        _historyService = historyService;
        _logger = logger;
    }

    /// <summary>
    /// Get workflow execution history with pagination.
    /// </summary>
    /// <param name="workflowId">Workflow ID</param>
    /// <param name="namespaceId">Namespace ID (default: "default")</param>
    /// <param name="runId">Run ID (optional - gets current run if not specified)</param>
    /// <param name="fromEventId">Starting event ID (default: 1)</param>
    /// <param name="maxEvents">Maximum events to return (default: 100, max: 1000)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Workflow history events</returns>
    [HttpGet]
    [ProducesResponseType(typeof(GetHistoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetHistory(
        [FromRoute] string workflowId,
        [FromQuery] string namespaceId = "default",
        [FromQuery] string? runId = null,
        [FromQuery] long fromEventId = 1,
        [FromQuery] int maxEvents = 100,
        CancellationToken cancellationToken = default)
    {
        // Validate parameters
        if (maxEvents < 1 || maxEvents > 1000)
        {
            return BadRequest(new ErrorResponse
            {
                Message = "maxEvents must be between 1 and 1000",
                Code = "INVALID_REQUEST"
            });
        }

        if (fromEventId < 1)
        {
            return BadRequest(new ErrorResponse
            {
                Message = "fromEventId must be >= 1",
                Code = "INVALID_REQUEST"
            });
        }

        // If no runId provided, need to look up current run
        // For Phase 1, we'll require runId to be specified
        if (string.IsNullOrEmpty(runId))
        {
            return BadRequest(new ErrorResponse
            {
                Message = "runId is required in Phase 1",
                Code = "INVALID_REQUEST"
            });
        }

        var request = new GetHistoryRequest
        {
            NamespaceId = namespaceId,
            WorkflowId = workflowId,
            RunId = runId,
            FromEventId = fromEventId,
            MaxEvents = maxEvents
        };

        var result = await _historyService.GetHistoryAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            _logger.LogError("Failed to get history for workflow {WorkflowId}/{RunId}: {Error}",
                workflowId, runId, result.Error?.Message);

            return NotFound(new ErrorResponse
            {
                Message = $"History not found for workflow '{workflowId}'",
                Code = "HISTORY_NOT_FOUND"
            });
        }

        _logger.LogDebug("Retrieved {Count} history events for workflow {WorkflowId}/{RunId}",
            result.Value.Events.Count, workflowId, runId);

        return Ok(result.Value);
    }

    /// <summary>
    /// Validate workflow history integrity.
    /// </summary>
    /// <param name="workflowId">Workflow ID</param>
    /// <param name="namespaceId">Namespace ID (default: "default")</param>
    /// <param name="runId">Run ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Validation result</returns>
    [HttpGet("validate")]
    [ProducesResponseType(typeof(ValidateHistoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ValidateHistory(
        [FromRoute] string workflowId,
        [FromQuery] string namespaceId = "default",
        [FromQuery] string? runId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(runId))
        {
            return BadRequest(new ErrorResponse
            {
                Message = "runId is required",
                Code = "INVALID_REQUEST"
            });
        }

        var result = await _historyService.ValidateHistoryAsync(
            namespaceId,
            workflowId,
            runId,
            cancellationToken);

        if (result.IsFailure)
        {
            _logger.LogError("Failed to validate history for workflow {WorkflowId}/{RunId}: {Error}",
                workflowId, runId, result.Error?.Message);

            return BadRequest(new ErrorResponse
            {
                Message = result.Error?.Message ?? "Validation failed",
                Code = result.Error?.Code ?? "VALIDATION_ERROR"
            });
        }

        return Ok(new ValidateHistoryResponse
        {
            IsValid = result.Value,
            Message = result.Value
                ? "History is valid with no sequence gaps"
                : "History contains sequence gaps"
        });
    }
}

/// <summary>
/// Response from history validation.
/// </summary>
public sealed record ValidateHistoryResponse
{
    public required bool IsValid { get; init; }
    public required string Message { get; init; }
}
