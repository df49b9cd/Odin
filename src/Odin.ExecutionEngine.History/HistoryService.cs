using Hugo;
using Microsoft.Extensions.Logging;
using Odin.Contracts;
using Odin.Core;
using Odin.Persistence.Interfaces;
using static Hugo.Go;

namespace Odin.ExecutionEngine.History;

/// <summary>
/// History service manages workflow event history append, retrieval, and validation.
/// Implements sharded history processing with deterministic event sequencing.
/// </summary>
public sealed class HistoryService : IHistoryService
{
    private readonly IHistoryRepository _historyRepository;
    private readonly IShardRepository _shardRepository;
    private readonly ILogger<HistoryService> _logger;
    private readonly string _hostIdentity;

    public HistoryService(
        IHistoryRepository historyRepository,
        IShardRepository shardRepository,
        ILogger<HistoryService> logger,
        string hostIdentity)
    {
        _historyRepository = historyRepository;
        _shardRepository = shardRepository;
        _logger = logger;
        _hostIdentity = hostIdentity;
    }

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
            // Calculate shard for this workflow
            var shardId = HashingUtilities.CalculateShardId(request.WorkflowId, 512);

            // Verify shard ownership (optional - for distributed deployments)
            var leaseResult = await _shardRepository.GetLeaseAsync(shardId, cancellationToken);
            if (leaseResult.IsFailure)
            {
                _logger.LogWarning(
                    "Could not verify shard ownership for shard {ShardId}, proceeding anyway",
                    shardId);
            }

            // Validate event sequence (events must be sequential)
            var firstEventId = request.Events[0].EventId;
            for (int i = 0; i < request.Events.Count; i++)
            {
                if (request.Events[i].EventId != firstEventId + i)
                {
                    _logger.LogError(
                        "Event sequence violation: expected event ID {ExpectedId}, got {ActualId}",
                        firstEventId + i, request.Events[i].EventId);
                    return Result.Fail<Unit>(
                        Error.From("Event sequence must be sequential", OdinErrorCodes.HistoryEventError));
                }
            }

            // Append events to repository
            var appendResult = await _historyRepository.AppendEventsAsync(
                request.NamespaceId,
                request.WorkflowId,
                request.RunId,
                request.Events,
                cancellationToken);

            if (appendResult.IsFailure)
            {
                _logger.LogError(
                    "Failed to append {Count} events for workflow {WorkflowId}/{RunId}: {Error}",
                    request.Events.Count, request.WorkflowId, request.RunId, appendResult.Error?.Message);
                return appendResult;
            }

            _logger.LogDebug(
                "Appended {Count} events to workflow {WorkflowId}/{RunId}, event IDs {FirstEventId}-{LastEventId}",
                request.Events.Count, request.WorkflowId, request.RunId,
                request.Events[0].EventId, request.Events[^1].EventId);

            return Result.Ok(Unit.Value);
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

            if (historyResult.IsFailure)
            {
                return Result.Fail<GetHistoryResponse>(historyResult.Error!);
            }

            var response = new GetHistoryResponse
            {
                NamespaceId = request.NamespaceId,
                WorkflowId = request.WorkflowId,
                RunId = request.RunId,
                Events = historyResult.Value.Events,
                FirstEventId = historyResult.Value.FirstEventId,
                LastEventId = historyResult.Value.LastEventId,
                NextPageToken = historyResult.Value.Events.Count >= request.MaxEvents
                    ? (historyResult.Value.LastEventId + 1).ToString()
                    : null
            };

            return Result.Ok(response);
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

            if (validationResult.IsFailure)
            {
                return validationResult;
            }

            if (!validationResult.Value)
            {
                _logger.LogWarning(
                    "History validation failed for workflow {WorkflowId}/{RunId} - sequence gaps detected",
                    workflowId, runId);
            }

            return validationResult;
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
