using Hugo;
using Microsoft.AspNetCore.Mvc;
using Odin.ExecutionEngine.History;
using static Hugo.Functional;

namespace Odin.ControlPlane.Api.Controllers;

/// <summary>
/// Workflow history query endpoints.
/// </summary>
[ApiController]
[Route("api/v1/workflows/{workflowId}/history")]
[Produces("application/json")]
public sealed class HistoryController(
    IHistoryService historyService,
    ILogger<HistoryController> logger) : ControllerBase
{
    private readonly IHistoryService _historyService = historyService;
    private readonly ILogger<HistoryController> _logger = logger;

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
        var historyPipeline = await Go.Ok(new
        {
            WorkflowId = workflowId,
            NamespaceId = namespaceId,
            RunId = runId,
            FromEventId = fromEventId,
            MaxEvents = maxEvents
        })
            .Ensure(static payload => payload.MaxEvents is >= 1 and <= 1000,
                static _ => Error.From("maxEvents must be between 1 and 1000", "INVALID_REQUEST"))
            .Ensure(static payload => payload.FromEventId >= 1,
                static _ => Error.From("fromEventId must be >= 1", "INVALID_REQUEST"))
            .Ensure(static payload => !string.IsNullOrWhiteSpace(payload.RunId),
                static _ => Error.From("runId is required in Phase 1", "INVALID_REQUEST"))
            .Map(payload => new GetHistoryRequest
            {
                NamespaceId = payload.NamespaceId,
                WorkflowId = payload.WorkflowId,
                RunId = payload.RunId!,
                FromEventId = payload.FromEventId,
                MaxEvents = payload.MaxEvents
            })
            .ThenAsync((request, ct) => _historyService.GetHistoryAsync(request, ct), cancellationToken)
            .ConfigureAwait(false);

        var historyResult = historyPipeline
            .OnSuccess(response => _logger.LogDebug(
                "Retrieved {Count} history events for workflow {WorkflowId}/{RunId}",
                response.Events.Count,
                workflowId,
                runId))
            .OnFailure(error => _logger.LogError(
                "Failed to get history for workflow {WorkflowId}/{RunId}: {Error}",
                workflowId,
                runId,
                error.Message));

        return historyResult.Match<IActionResult>(
            response => Ok(response),
            error => string.Equals(error.Code, "INVALID_REQUEST", StringComparison.OrdinalIgnoreCase)
                ? BadRequest(AsErrorResponse(error, "INVALID_REQUEST", error.Message ?? "Invalid history request"))
                : NotFound(AsErrorResponse(
                    error,
                    error.Code ?? "HISTORY_NOT_FOUND",
                    $"History not found for workflow '{workflowId}'")));
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
        var validationPipeline = await Go.Ok(runId)
            .Ensure(static id => !string.IsNullOrWhiteSpace(id),
                static _ => Error.From("runId is required", "INVALID_REQUEST"))
            .Map(id => id!)
            .ThenAsync((validatedRunId, ct) => _historyService.ValidateHistoryAsync(
                namespaceId,
                workflowId,
                validatedRunId,
                ct), cancellationToken)
            .ConfigureAwait(false);

        var validationResult = validationPipeline
            .OnFailure(error => _logger.LogError(
                "Failed to validate history for workflow {WorkflowId}/{RunId}: {Error}",
                workflowId,
                runId,
                error.Message));

        return validationResult.Match<IActionResult>(
            isValid => Ok(new ValidateHistoryResponse
            {
                IsValid = isValid,
                Message = isValid
                    ? "History is valid with no sequence gaps"
                    : "History contains sequence gaps"
            }),
            error => BadRequest(AsErrorResponse(
                error,
                error.Code ?? "VALIDATION_ERROR",
                error.Message ?? "Validation failed")));
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
/// Response from history validation.
/// </summary>
public sealed record ValidateHistoryResponse
{
    public required bool IsValid { get; init; }
    public required string Message { get; init; }
}
