using Hugo;
using Odin.Contracts;
using static Hugo.Go;

namespace Odin.Persistence.Interfaces;

/// <summary>
/// Repository for immutable workflow history event storage.
/// Implements event sourcing pattern for workflow execution history.
/// </summary>
public interface IHistoryRepository
{
    /// <summary>
    /// Appends a batch of history events to a workflow's event log.
    /// Events must be appended in order with sequential event IDs.
    /// </summary>
    Task<Result<Unit>> AppendEventsAsync(
        string namespaceId,
        string workflowId,
        string runId,
        IReadOnlyList<HistoryEvent> events,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves complete history for a workflow execution.
    /// </summary>
    Task<Result<WorkflowHistoryBatch>> GetHistoryAsync(
        string namespaceId,
        string workflowId,
        string runId,
        long fromEventId = 1,
        int maxEvents = 1000,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves history events starting from a specific event ID.
    /// Used for incremental history loading during workflow execution.
    /// </summary>
    Task<Result<IReadOnlyList<HistoryEvent>>> GetEventsFromAsync(
        string namespaceId,
        string workflowId,
        string runId,
        long fromEventId,
        int maxEvents = 1000,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a single history event by event ID.
    /// </summary>
    Task<Result<HistoryEvent>> GetEventAsync(
        string namespaceId,
        string workflowId,
        string runId,
        long eventId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the count of history events for a workflow.
    /// </summary>
    Task<Result<long>> GetEventCountAsync(
        string namespaceId,
        string workflowId,
        string runId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Archives history events older than retention period.
    /// Moves events to cold storage or deletes based on namespace configuration.
    /// </summary>
    Task<Result<int>> ArchiveOldEventsAsync(
        string namespaceId,
        DateTimeOffset olderThan,
        int batchSize = 1000,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that event IDs are sequential and complete.
    /// Returns error if gaps detected.
    /// </summary>
    Task<Result<bool>> ValidateEventSequenceAsync(
        string namespaceId,
        string workflowId,
        string runId,
        CancellationToken cancellationToken = default);
}
