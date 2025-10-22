using System.Collections.Concurrent;
using Hugo;
using Odin.Contracts;
using Odin.Core;
using Odin.Persistence.Interfaces;
using static Hugo.Go;

namespace Odin.Persistence.InMemory;

/// <summary>
/// In-memory history repository storing workflow events in process memory.
/// </summary>
public sealed class InMemoryHistoryRepository : IHistoryRepository
{
    private readonly ConcurrentDictionary<(string NamespaceId, string WorkflowId, string RunId), List<HistoryEvent>> _events = new();

    public Task<Result<Unit>> AppendEventsAsync(
        string namespaceId,
        string workflowId,
        string runId,
        IReadOnlyList<HistoryEvent> events,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(events);

        if (events.Count == 0)
        {
            return Task.FromResult(Result.Ok(Unit.Value));
        }

        var key = (namespaceId, workflowId, runId);
        var list = _events.GetOrAdd(key, _ => new List<HistoryEvent>());

        lock (list)
        {
            var lastEventId = list.Count == 0 ? 0 : list[^1].EventId;
            var expectedNextId = lastEventId + 1;

            foreach (var historyEvent in events)
            {
                if (historyEvent.EventId != expectedNextId)
                {
                    return Task.FromResult(Result.Fail<Unit>(
                        Error.From(
                            $"History events out of sequence. Expected event ID {expectedNextId}, received {historyEvent.EventId}",
                            OdinErrorCodes.HistoryEventError)));
                }

                expectedNextId++;
            }

            list.AddRange(events);
        }

        return Task.FromResult(Result.Ok(Unit.Value));
    }

    public Task<Result<WorkflowHistoryBatch>> GetHistoryAsync(
        string namespaceId,
        string workflowId,
        string runId,
        long fromEventId,
        int maxEvents,
        CancellationToken cancellationToken = default)
    {
        var eventsResult = GetEvents(namespaceId, workflowId, runId);
        if (eventsResult.IsFailure)
        {
            return Task.FromResult(Result.Fail<WorkflowHistoryBatch>(eventsResult.Error!));
        }

        var events = eventsResult.Value;
        var filtered = events
            .Where(e => e.EventId >= fromEventId)
            .OrderBy(e => e.EventId)
            .Take(maxEvents)
            .ToList();

        var namespaceGuid = Guid.TryParse(namespaceId, out var ns) ? ns : Guid.Empty;
        var runGuid = Guid.TryParse(runId, out var run) ? run : Guid.Empty;

        var batch = new WorkflowHistoryBatch
        {
            NamespaceId = namespaceGuid,
            WorkflowId = workflowId,
            RunId = runGuid,
            FirstEventId = filtered.Count > 0 ? filtered[0].EventId : fromEventId,
            LastEventId = filtered.Count > 0 ? filtered[^1].EventId : fromEventId - 1,
            Events = filtered,
            IsLastBatch = filtered.Count < maxEvents
        };

        return Task.FromResult(Result.Ok(batch));
    }

    public Task<Result<IReadOnlyList<HistoryEvent>>> GetEventsFromAsync(
        string namespaceId,
        string workflowId,
        string runId,
        long fromEventId,
        int maxEvents = 1000,
        CancellationToken cancellationToken = default)
    {
        var eventsResult = GetEvents(namespaceId, workflowId, runId);
        if (eventsResult.IsFailure)
        {
            return Task.FromResult(Result.Fail<IReadOnlyList<HistoryEvent>>(eventsResult.Error!));
        }

        var events = eventsResult.Value
            .Where(e => e.EventId >= fromEventId)
            .OrderBy(e => e.EventId)
            .Take(maxEvents)
            .ToList();

        return Task.FromResult(Result.Ok<IReadOnlyList<HistoryEvent>>(events));
    }

    public Task<Result<HistoryEvent>> GetEventAsync(
        string namespaceId,
        string workflowId,
        string runId,
        long eventId,
        CancellationToken cancellationToken = default)
    {
        var eventsResult = GetEvents(namespaceId, workflowId, runId);
        if (eventsResult.IsFailure)
        {
            return Task.FromResult(Result.Fail<HistoryEvent>(eventsResult.Error!));
        }

        var match = eventsResult.Value.FirstOrDefault(e => e.EventId == eventId);

        if (match is null)
        {
            return Task.FromResult(Result.Fail<HistoryEvent>(
                Error.From($"History event {eventId} not found", OdinErrorCodes.HistoryEventError)));
        }

        return Task.FromResult(Result.Ok(match));
    }

    public Task<Result<long>> GetEventCountAsync(
        string namespaceId,
        string workflowId,
        string runId,
        CancellationToken cancellationToken = default)
    {
        var eventsResult = GetEvents(namespaceId, workflowId, runId);
        if (eventsResult.IsFailure)
        {
            return Task.FromResult(Result.Fail<long>(eventsResult.Error!));
        }

        return Task.FromResult(Result.Ok((long)eventsResult.Value.Count));
    }

    public Task<Result<int>> ArchiveOldEventsAsync(
        string namespaceId,
        DateTimeOffset olderThan,
        int batchSize = 1000,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceId);

        var totalRemoved = 0;

        foreach (var (key, list) in _events)
        {
            if (key.NamespaceId != namespaceId)
            {
                continue;
            }

            lock (list)
            {
                var originalCount = list.Count;
                list.RemoveAll(evt => evt.EventTimestamp < olderThan);
                totalRemoved += originalCount - list.Count;
            }
        }

        return Task.FromResult(Result.Ok(totalRemoved));
    }

    public Task<Result<bool>> ValidateEventSequenceAsync(
        string namespaceId,
        string workflowId,
        string runId,
        CancellationToken cancellationToken = default)
    {
        var eventsResult = GetEvents(namespaceId, workflowId, runId);
        if (eventsResult.IsFailure)
        {
            return Task.FromResult(Result.Fail<bool>(eventsResult.Error!));
        }

        var events = eventsResult.Value;
        var expectedId = 1L;

        foreach (var historyEvent in events.OrderBy(e => e.EventId))
        {
            if (historyEvent.EventId != expectedId)
            {
                return Task.FromResult(Result.Ok(false));
            }

            expectedId++;
        }

        return Task.FromResult(Result.Ok(true));
    }

    private Result<List<HistoryEvent>> GetEvents(
        string namespaceId,
        string workflowId,
        string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        var key = (namespaceId, workflowId, runId);

        if (!_events.TryGetValue(key, out var list))
        {
            return Result.Ok(new List<HistoryEvent>());
        }

        lock (list)
        {
            return Result.Ok(list.ToList());
        }
    }
}
