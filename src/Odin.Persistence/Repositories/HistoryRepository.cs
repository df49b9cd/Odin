using Hugo;
using Odin.Contracts;
using Odin.Core;
using Odin.Persistence.Interfaces;
using static Hugo.Go;

namespace Odin.Persistence.Repositories;

/// <summary>
/// PostgreSQL/MySQL implementation of history repository.
/// Phase 1: Stub implementation - to be completed in Phase 2.
/// </summary>
public sealed class HistoryRepository : IHistoryRepository
{
    public Task<Result<Unit>> AppendEventsAsync(string namespaceId, string workflowId, string runId, IReadOnlyList<HistoryEvent> events, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Ok(Unit.Value));
    }

    public Task<Result<WorkflowHistoryBatch>> GetHistoryAsync(string namespaceId, string workflowId, string runId, long fromEventId = 1, int maxEvents = 1000, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Fail<WorkflowHistoryBatch>(Error.From("Not implemented", OdinErrorCodes.HistoryEventError)));
    }

    public Task<Result<IReadOnlyList<HistoryEvent>>> GetEventsFromAsync(string namespaceId, string workflowId, string runId, long fromEventId, int maxEvents = 1000, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Ok<IReadOnlyList<HistoryEvent>>(Array.Empty<HistoryEvent>()));
    }

    public Task<Result<HistoryEvent>> GetEventAsync(string namespaceId, string workflowId, string runId, long eventId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Fail<HistoryEvent>(Error.From("Not implemented", OdinErrorCodes.HistoryEventError)));
    }

    public Task<Result<long>> GetEventCountAsync(string namespaceId, string workflowId, string runId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Ok(0L));
    }

    public Task<Result<int>> ArchiveOldEventsAsync(string namespaceId, DateTimeOffset olderThan, int batchSize = 1000, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Ok(0));
    }

    public Task<Result<bool>> ValidateEventSequenceAsync(string namespaceId, string workflowId, string runId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Ok(true));
    }
}
