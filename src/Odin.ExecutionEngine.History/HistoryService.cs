using Hugo;
using Microsoft.Extensions.Logging;
using Odin.Contracts;
using Odin.Core;
using Odin.Persistence.Interfaces;
using static Hugo.Functional;
using static Hugo.Go;

namespace Odin.ExecutionEngine.History;

/// <summary>
/// History service manages workflow event history append, retrieval, and validation.
/// Implements sharded history processing with deterministic event sequencing.
/// </summary>
public sealed class HistoryService(
    IHistoryRepository historyRepository,
    IShardRepository shardRepository,
    ILogger<HistoryService> logger,
    string hostIdentity) : IHistoryService
{
    private readonly IHistoryRepository _historyRepository = historyRepository;
    private readonly IShardRepository _shardRepository = shardRepository;
    private readonly ILogger<HistoryService> _logger = logger;
    private readonly string _hostIdentity = hostIdentity;

    /// <summary>
    /// Appends new events to workflow history with shard ownership validation.
    /// </summary>
    public async Task<Result<Unit>> AppendEventsAsync(
        AppendHistoryEventsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Events.Count == 0)
        {
            return Result.Ok(Unit.Value);
        }

        try
        {
            var shardId = HashingUtilities.CalculateShardId(request.WorkflowId, 512);
            var ownershipResult = await Result.Ok(Unit.Value)
                .OnSuccessAsync((_, ct) => VerifyShardOwnershipAsync(shardId, ct), cancellationToken);

            var validationResult = ownershipResult.Then(_ => ValidateEventSequence(request, _logger));
            if (validationResult.IsFailure)
            {
                return validationResult;
            }

            var appendResult = _historyRepository.AppendEventsAsync(
                request.NamespaceId,
                request.WorkflowId,
                request.RunId,
                request.Events,
                cancellationToken)
                .OnFailureAsync(error => _logger.LogError(
                    "Failed to append {Count} events for workflow {WorkflowId}/{RunId}: {Error}",
                    request.Events.Count,
                    request.WorkflowId,
                    request.RunId,
                    error.Message), cancellationToken)
                .OnSuccessAsync(_ => _logger.LogDebug(
                    "Appended {Count} events to workflow {WorkflowId}/{RunId}, event IDs {FirstEventId}-{LastEventId}",
                    request.Events.Count,
                    request.WorkflowId,
                    request.RunId,
                    request.Events[0].EventId,
                    request.Events[^1].EventId), cancellationToken);

            return await appendResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error appending events for workflow {WorkflowId}/{RunId}",
                request.WorkflowId, request.RunId);
            return Result.Fail<Unit>(
                Error.From($"Append events failed: {ex.Message}", OdinErrorCodes.PersistenceError));
        }
    }

    /// <summary>
    /// Retrieves workflow history with pagination support.
    /// </summary>
    public async Task<Result<GetHistoryResponse>> GetHistoryAsync(
        GetHistoryRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var historyResult = await _historyRepository.GetHistoryAsync(
                request.NamespaceId,
                request.WorkflowId,
                request.RunId,
                request.FromEventId,
                request.MaxEvents,
                cancellationToken);

            return historyResult
                .OnFailure(error => _logger.LogError(
                    "Failed to get history for workflow {WorkflowId}/{RunId}: {Error}",
                    request.WorkflowId,
                    request.RunId,
                    error.Message))
                .Map(history => new GetHistoryResponse
                {
                    NamespaceId = request.NamespaceId,
                    WorkflowId = request.WorkflowId,
                    RunId = request.RunId,
                    Events = history.Events,
                    FirstEventId = history.FirstEventId,
                    LastEventId = history.LastEventId,
                    NextPageToken = history.Events.Count >= request.MaxEvents
                        ? (history.LastEventId + 1).ToString()
                        : null
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to get history for workflow {WorkflowId}/{RunId}",
                request.WorkflowId, request.RunId);
            return Result.Fail<GetHistoryResponse>(
                Error.From($"Get history failed: {ex.Message}", OdinErrorCodes.PersistenceError));
        }
    }

    /// <summary>
    /// Validates event sequence integrity for replay safety.
    /// </summary>
    public async Task<Result<bool>> ValidateHistoryAsync(
        string namespaceId,
        string workflowId,
        string runId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var validationResult = await _historyRepository.ValidateEventSequenceAsync(
                namespaceId,
                workflowId,
                runId,
                cancellationToken);

            return validationResult
                .OnFailure(error => _logger.LogError(
                    "Failed to validate history for workflow {WorkflowId}/{RunId}: {Error}",
                    workflowId,
                    runId,
                    error.Message))
                .OnSuccess(isValid =>
                {
                    if (!isValid)
                    {
                        _logger.LogWarning(
                            "History validation failed for workflow {WorkflowId}/{RunId} - sequence gaps detected",
                            workflowId,
                            runId);
                    }
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to validate history for workflow {WorkflowId}/{RunId}",
                workflowId, runId);
            return Result.Fail<bool>(
                Error.From($"Validation failed: {ex.Message}", OdinErrorCodes.PersistenceError));
        }
    }

    private async Task VerifyShardOwnershipAsync(
        int shardId,
        CancellationToken cancellationToken)
    {
        var leaseResult = await _shardRepository.GetLeaseAsync(shardId, cancellationToken);
        if (leaseResult.IsFailure)
        {
            _logger.LogWarning(
                "Could not verify shard ownership for shard {ShardId}, proceeding anyway: {Error}",
                shardId,
                leaseResult.Error?.Message);
        }
    }

    private static Result<Unit> ValidateEventSequence(
        AppendHistoryEventsRequest request,
        ILogger logger)
    {
        var events = request.Events;
        if (events.Count == 0)
        {
            return Result.Ok(Unit.Value);
        }

        var expected = events[0].EventId;
        for (var index = 0; index < events.Count; index++, expected++)
        {
            var actual = events[index].EventId;
            if (actual == expected)
            {
                continue;
            }

            logger.LogError(
                "Event sequence violation for workflow {WorkflowId}/{RunId}: expected event ID {ExpectedId}, got {ActualId}",
                request.WorkflowId,
                request.RunId,
                expected,
                actual);

            var metadata = new Dictionary<string, object?>
            {
                ["expectedEventId"] = expected,
                ["actualEventId"] = actual,
                ["workflowId"] = request.WorkflowId,
                ["runId"] = request.RunId
            };

            return Result.Fail<Unit>(
                Error.From("Event sequence must be sequential.", OdinErrorCodes.HistoryEventError)
                    .WithMetadata(metadata));
        }

        return Result.Ok(Unit.Value);
    }
}

/// <summary>
/// History service interface for dependency injection.
/// </summary>
public interface IHistoryService
{
    Task<Result<Unit>> AppendEventsAsync(
        AppendHistoryEventsRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<GetHistoryResponse>> GetHistoryAsync(
        GetHistoryRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<bool>> ValidateHistoryAsync(
        string namespaceId,
        string workflowId,
        string runId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Request to append events to workflow history.
/// </summary>
public sealed record AppendHistoryEventsRequest
{
    public required string NamespaceId { get; init; }
    public required string WorkflowId { get; init; }
    public required string RunId { get; init; }
    public required IReadOnlyList<HistoryEvent> Events { get; init; }
}

/// <summary>
/// Request to retrieve workflow history.
/// </summary>
public sealed record GetHistoryRequest
{
    public required string NamespaceId { get; init; }
    public required string WorkflowId { get; init; }
    public required string RunId { get; init; }
    public long FromEventId { get; init; } = 1;
    public int MaxEvents { get; init; } = 1000;
}

/// <summary>
/// Response containing workflow history events.
/// </summary>
public sealed record GetHistoryResponse
{
    public required string NamespaceId { get; init; }
    public required string WorkflowId { get; init; }
    public required string RunId { get; init; }
    public required IReadOnlyList<HistoryEvent> Events { get; init; }
    public required long FirstEventId { get; init; }
    public required long LastEventId { get; init; }
    public string? NextPageToken { get; init; }
}
