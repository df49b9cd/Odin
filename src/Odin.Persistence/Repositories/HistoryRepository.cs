using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using Dapper;
using Hugo;
using Microsoft.Extensions.Logging;
using Npgsql;
using Odin.Contracts;
using Odin.Core;
using Odin.Persistence.Interfaces;
using static Hugo.Go;

namespace Odin.Persistence.Repositories;

/// <summary>
/// PostgreSQL implementation of immutable workflow history persistence.
/// </summary>
public sealed class HistoryRepository(
    IDbConnectionFactory connectionFactory,
    ILogger<HistoryRepository> logger) : IHistoryRepository
{
    private readonly IDbConnectionFactory _connectionFactory = connectionFactory;
    private readonly ILogger<HistoryRepository> _logger = logger;

    public async Task<Result<Unit>> AppendEventsAsync(
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
            return Result.Ok(Unit.Value);
        }

        if (!TryParseIdentifiers(namespaceId, workflowId, runId, out var nsGuid, out var runGuid, out var error))
        {
            return Result.Fail<Unit>(error!);
        }

        var orderedEvents = events.OrderBy(e => e.EventId).ToList();

        if (!IsSequential(orderedEvents))
        {
            return Result.Fail<Unit>(
                Error.From("History events must be sequential with no gaps.", OdinErrorCodes.HistoryEventError));
        }

        var connectionResult = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        if (connectionResult.IsFailure)
        {
            return Result.Fail<Unit>(connectionResult.Error!);
        }

        using var connection = connectionResult.Value;
        using var transaction = connection.BeginTransaction();

        try
        {
            var lastEventId = await connection.QuerySingleOrDefaultAsync<long?>(
                @"SELECT event_id
                  FROM history_events
                  WHERE namespace_id = @NamespaceId
                    AND workflow_id = @WorkflowId
                    AND run_id = @RunId
                  ORDER BY event_id DESC
                  LIMIT 1
                  FOR UPDATE",
                new
                {
                    NamespaceId = nsGuid,
                    WorkflowId = workflowId,
                    RunId = runGuid
                },
                transaction) ?? 0;

            var expectedNext = lastEventId == 0 ? 1 : lastEventId + 1;

            if (orderedEvents[0].EventId != expectedNext)
            {
                transaction.Rollback();
                return Result.Fail<Unit>(
                    Error.From(
                        $"Expected next history event id {expectedNext} but received {orderedEvents[0].EventId}.",
                        OdinErrorCodes.HistoryEventError));
            }

            const string insertSql = @"
INSERT INTO history_events (
    namespace_id,
    workflow_id,
    run_id,
    event_id,
    event_type,
    event_timestamp,
    task_id,
    version,
    event_data
) VALUES (
    @NamespaceId,
    @WorkflowId,
    @RunId,
    @EventId,
    @EventType,
    @EventTimestamp,
    @TaskId,
    @Version,
    CAST(@EventData AS JSONB)
)";

            foreach (var historyEvent in orderedEvents)
            {
                await connection.ExecuteAsync(
                    insertSql,
                    new
                    {
                        NamespaceId = nsGuid,
                        WorkflowId = workflowId,
                        RunId = runGuid,
                        historyEvent.EventId,
                        historyEvent.EventType,
                        historyEvent.EventTimestamp,
                        historyEvent.TaskId,
                        historyEvent.Version,
                        EventData = historyEvent.EventData.RootElement.GetRawText()
                    },
                    transaction);
            }

            transaction.Commit();
            return Result.Ok(Unit.Value);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            transaction.Rollback();
            _logger.LogWarning(
                ex,
                "Duplicate history event detected (namespaceId={NamespaceId}, workflowId={WorkflowId}, runId={RunId}).",
                namespaceId,
                workflowId,
                runId);

            return Result.Fail<Unit>(
                Error.From("Duplicate history event detected.", OdinErrorCodes.HistoryEventError));
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            _logger.LogError(
                ex,
                "Failed to append history events (namespaceId={NamespaceId}, workflowId={WorkflowId}, runId={RunId}).",
                namespaceId,
                workflowId,
                runId);

            return Result.Fail<Unit>(
                Error.From($"Database error: {ex.Message}", OdinErrorCodes.PersistenceError));
        }
    }

    public async Task<Result<WorkflowHistoryBatch>> GetHistoryAsync(
        string namespaceId,
        string workflowId,
        string runId,
        long fromEventId = 1,
        int maxEvents = 1000,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseIdentifiers(namespaceId, workflowId, runId, out var nsGuid, out var runGuid, out var error))
        {
            return Result.Fail<WorkflowHistoryBatch>(error!);
        }

        var take = NormalizeLimit(maxEvents);

        var connectionResult = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        if (connectionResult.IsFailure)
        {
            return Result.Fail<WorkflowHistoryBatch>(connectionResult.Error!);
        }

        using var connection = connectionResult.Value;

        var rows = (await connection.QueryAsync<HistoryEventRow>(
            @"SELECT
                event_id AS EventId,
                event_type AS EventType,
                event_timestamp AS EventTimestamp,
                task_id AS TaskId,
                version,
                event_data AS EventData
              FROM history_events
              WHERE namespace_id = @NamespaceId
                AND workflow_id = @WorkflowId
                AND run_id = @RunId
                AND event_id >= @FromEventId
              ORDER BY event_id ASC
              LIMIT @Limit",
            new
            {
                NamespaceId = nsGuid,
                WorkflowId = workflowId,
                RunId = runGuid,
                FromEventId = Math.Max(1, fromEventId),
                Limit = take
            })).ToList();

        var events = rows.Select(r => r.ToModel()).ToList();

        if (events.Count == 0)
        {
            return Result.Ok(new WorkflowHistoryBatch
            {
                NamespaceId = nsGuid,
                WorkflowId = workflowId,
                RunId = runGuid,
                FirstEventId = Math.Max(1, fromEventId),
                LastEventId = Math.Max(0, fromEventId - 1),
                Events = Array.Empty<HistoryEvent>(),
                IsLastBatch = true
            });
        }

        return Result.Ok(new WorkflowHistoryBatch
        {
            NamespaceId = nsGuid,
            WorkflowId = workflowId,
            RunId = runGuid,
            FirstEventId = events.First().EventId,
            LastEventId = events.Last().EventId,
            Events = events,
            IsLastBatch = events.Count < take
        });
    }

    public async Task<Result<IReadOnlyList<HistoryEvent>>> GetEventsFromAsync(
        string namespaceId,
        string workflowId,
        string runId,
        long fromEventId,
        int maxEvents = 1000,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseIdentifiers(namespaceId, workflowId, runId, out var nsGuid, out var runGuid, out var error))
        {
            return Result.Fail<IReadOnlyList<HistoryEvent>>(error!);
        }

        var take = NormalizeLimit(maxEvents);

        var connectionResult = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        if (connectionResult.IsFailure)
        {
            return Result.Fail<IReadOnlyList<HistoryEvent>>(connectionResult.Error!);
        }

        using var connection = connectionResult.Value;

        var rows = await connection.QueryAsync<HistoryEventRow>(
            @"SELECT
                event_id AS EventId,
                event_type AS EventType,
                event_timestamp AS EventTimestamp,
                task_id AS TaskId,
                version,
                event_data AS EventData
              FROM history_events
              WHERE namespace_id = @NamespaceId
                AND workflow_id = @WorkflowId
                AND run_id = @RunId
                AND event_id >= @FromEventId
              ORDER BY event_id ASC
              LIMIT @Limit",
            new
            {
                NamespaceId = nsGuid,
                WorkflowId = workflowId,
                RunId = runGuid,
                FromEventId = Math.Max(1, fromEventId),
                Limit = take
            });

        return Result.Ok<IReadOnlyList<HistoryEvent>>(rows.Select(r => r.ToModel()).ToList());
    }

    public async Task<Result<HistoryEvent>> GetEventAsync(
        string namespaceId,
        string workflowId,
        string runId,
        long eventId,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseIdentifiers(namespaceId, workflowId, runId, out var nsGuid, out var runGuid, out var error))
        {
            return Result.Fail<HistoryEvent>(error!);
        }

        var connectionResult = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        if (connectionResult.IsFailure)
        {
            return Result.Fail<HistoryEvent>(connectionResult.Error!);
        }

        using var connection = connectionResult.Value;

        var row = await connection.QuerySingleOrDefaultAsync<HistoryEventRow>(
            @"SELECT
                event_id AS EventId,
                event_type AS EventType,
                event_timestamp AS EventTimestamp,
                task_id AS TaskId,
                version,
                event_data AS EventData
              FROM history_events
              WHERE namespace_id = @NamespaceId
                AND workflow_id = @WorkflowId
                AND run_id = @RunId
                AND event_id = @EventId",
            new
            {
                NamespaceId = nsGuid,
                WorkflowId = workflowId,
                RunId = runGuid,
                EventId = eventId
            });

        if (row is null)
        {
            return Result.Fail<HistoryEvent>(
                Error.From("History event not found.", OdinErrorCodes.HistoryEventError));
        }

        return Result.Ok(row.ToModel());
    }

    public async Task<Result<long>> GetEventCountAsync(
        string namespaceId,
        string workflowId,
        string runId,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseIdentifiers(namespaceId, workflowId, runId, out var nsGuid, out var runGuid, out var error))
        {
            return Result.Fail<long>(error!);
        }

        var connectionResult = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        if (connectionResult.IsFailure)
        {
            return Result.Fail<long>(connectionResult.Error!);
        }

        using var connection = connectionResult.Value;

        var count = await connection.ExecuteScalarAsync<long>(
            @"SELECT COUNT(*)
              FROM history_events
              WHERE namespace_id = @NamespaceId
                AND workflow_id = @WorkflowId
                AND run_id = @RunId",
            new
            {
                NamespaceId = nsGuid,
                WorkflowId = workflowId,
                RunId = runGuid
            });

        return Result.Ok(count);
    }

    public async Task<Result<int>> ArchiveOldEventsAsync(
        string namespaceId,
        DateTimeOffset olderThan,
        int batchSize = 1000,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceId);

        if (!Guid.TryParse(namespaceId, out var nsGuid))
        {
            return Result.Fail<int>(
                Error.From("Invalid namespace id.", OdinErrorCodes.HistoryEventError));
        }

        var normalizedBatchSize = batchSize <= 0 ? 1000 : batchSize;

        var connectionResult = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        if (connectionResult.IsFailure)
        {
            return Result.Fail<int>(connectionResult.Error!);
        }

        using var connection = connectionResult.Value;

        var rowsAffected = await connection.ExecuteAsync(
            @"
DELETE FROM history_events
WHERE namespace_id = @NamespaceId
  AND event_timestamp < @OlderThan
  AND event_id IN (
        SELECT event_id
        FROM history_events
        WHERE namespace_id = @NamespaceId
          AND event_timestamp < @OlderThan
        ORDER BY event_id
        LIMIT @BatchSize
    )",
            new
            {
                NamespaceId = nsGuid,
                OlderThan = olderThan,
                BatchSize = normalizedBatchSize
            });

        return Result.Ok(rowsAffected);
    }

    public async Task<Result<bool>> ValidateEventSequenceAsync(
        string namespaceId,
        string workflowId,
        string runId,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseIdentifiers(namespaceId, workflowId, runId, out var nsGuid, out var runGuid, out var error))
        {
            return Result.Fail<bool>(error!);
        }

        var connectionResult = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        if (connectionResult.IsFailure)
        {
            return Result.Fail<bool>(connectionResult.Error!);
        }

        using var connection = connectionResult.Value;

        var gapCount = await connection.ExecuteScalarAsync<long>(
            @"
SELECT COUNT(*)
FROM (
    SELECT event_id,
           LAG(event_id) OVER (ORDER BY event_id) AS prev_event_id
    FROM history_events
    WHERE namespace_id = @NamespaceId
      AND workflow_id = @WorkflowId
      AND run_id = @RunId
) seq
WHERE prev_event_id IS NOT NULL
  AND event_id <> prev_event_id + 1",
            new
            {
                NamespaceId = nsGuid,
                WorkflowId = workflowId,
                RunId = runGuid
            });

        return Result.Ok(gapCount == 0);
    }

    private static bool TryParseIdentifiers(
        string namespaceId,
        string workflowId,
        string runId,
        out Guid namespaceGuid,
        out Guid runGuid,
        out Error? error)
    {
        var namespaceValid = Guid.TryParse(namespaceId, out namespaceGuid);
        var runValid = Guid.TryParse(runId, out runGuid);

        if (!namespaceValid || !runValid)
        {
            error = OdinErrors.WorkflowNotFound(workflowId, runId);
            return false;
        }

        error = null;
        return true;
    }

    private static bool IsSequential(IReadOnlyList<HistoryEvent> events)
    {
        for (var i = 1; i < events.Count; i++)
        {
            if (events[i].EventId != events[i - 1].EventId + 1)
            {
                return false;
            }
        }

        return true;
    }

    private static int NormalizeLimit(int maxEvents)
        => maxEvents <= 0 ? 1000 : Math.Min(maxEvents, 5000);

    private sealed class HistoryEventRow
    {
        public long EventId { get; set; }
        public string EventType { get; set; } = string.Empty;
        public DateTimeOffset EventTimestamp { get; set; }
        public long TaskId { get; set; }
        public long Version { get; set; }
        public string EventData { get; set; } = string.Empty;

        public HistoryEvent ToModel()
        {
            return new HistoryEvent
            {
                EventId = EventId,
                EventType = EventType,
                EventTimestamp = EventTimestamp,
                TaskId = TaskId,
                Version = Version,
                EventData = JsonDocument.Parse(EventData)
            };
        }
    }
}
