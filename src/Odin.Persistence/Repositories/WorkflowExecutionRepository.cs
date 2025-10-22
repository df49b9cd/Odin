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
using WorkflowExecution = Odin.Contracts.WorkflowExecution;
using ContractsWorkflowStatus = Odin.Contracts.WorkflowStatus;

namespace Odin.Persistence.Repositories;

/// <summary>
/// PostgreSQL implementation of workflow execution persistence.
/// </summary>
public sealed class WorkflowExecutionRepository(
    IDbConnectionFactory connectionFactory,
    ILogger<WorkflowExecutionRepository> logger) : IWorkflowExecutionRepository
{
    private readonly IDbConnectionFactory _connectionFactory = connectionFactory;
    private readonly ILogger<WorkflowExecutionRepository> _logger = logger;

    public async Task<Result<WorkflowExecution>> CreateAsync(
        WorkflowExecution execution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution);

        var connectionResult = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        if (connectionResult.IsFailure)
        {
            return Result.Fail<WorkflowExecution>(connectionResult.Error!);
        }

        using var connection = connectionResult.Value;

        var now = DateTimeOffset.UtcNow;
        var startedAt = execution.StartedAt == default ? now : execution.StartedAt;
        var shardId = execution.ShardId != 0
            ? execution.ShardId
            : HashingUtilities.CalculateShardId(execution.WorkflowId);
        var version = execution.Version == 0 ? 1 : execution.Version;

        var sql = @"
INSERT INTO workflow_executions (
    namespace_id,
    workflow_id,
    run_id,
    workflow_type,
    task_queue,
    workflow_state,
    execution_state,
    next_event_id,
    last_processed_event_id,
    workflow_timeout_seconds,
    run_timeout_seconds,
    task_timeout_seconds,
    retry_policy,
    cron_schedule,
    parent_workflow_id,
    parent_run_id,
    initiated_id,
    completion_event_id,
    memo,
    search_attributes,
    auto_reset_points,
    started_at,
    completed_at,
    last_updated_at,
    shard_id,
    version
)
VALUES (
    @NamespaceId,
    @WorkflowId,
    @RunId,
    @WorkflowType,
    @TaskQueue,
    @WorkflowState,
    @ExecutionState,
    @NextEventId,
    @LastProcessedEventId,
    @WorkflowTimeoutSeconds,
    @RunTimeoutSeconds,
    @TaskTimeoutSeconds,
    @RetryPolicy,
    @CronSchedule,
    @ParentWorkflowId,
    @ParentRunId,
    @InitiatedId,
    @CompletionEventId,
    @Memo,
    @SearchAttributes,
    @AutoResetPoints,
    @StartedAt,
    @CompletedAt,
    @LastUpdatedAt,
    @ShardId,
    @Version
)
RETURNING *
";

        var parameters = new
        {
            execution.NamespaceId,
            execution.WorkflowId,
            execution.RunId,
            execution.WorkflowType,
            execution.TaskQueue,
            WorkflowState = ToDatabaseState(execution.WorkflowState),
            ExecutionState = execution.ExecutionState,
            execution.NextEventId,
            execution.LastProcessedEventId,
            execution.WorkflowTimeoutSeconds,
            execution.RunTimeoutSeconds,
            execution.TaskTimeoutSeconds,
            RetryPolicy = ToJson(execution.RetryPolicy),
            execution.CronSchedule,
            execution.ParentWorkflowId,
            ParentRunId = execution.ParentRunId,
            execution.InitiatedId,
            execution.CompletionEventId,
            Memo = ToJson(execution.Memo),
            SearchAttributes = ToJson(execution.SearchAttributes),
            AutoResetPoints = ToJson(execution.AutoResetPoints),
            StartedAt = startedAt,
            execution.CompletedAt,
            LastUpdatedAt = now,
            ShardId = shardId,
            Version = version
        };

        try
        {
            var row = await connection.QuerySingleAsync<WorkflowExecutionRow>(sql, parameters);
            return Result.Ok(row.ToModel());
        }
        catch (PostgresException pgEx) when (pgEx.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            _logger.LogWarning(
                pgEx,
                "Workflow execution already exists (namespaceId={NamespaceId}, workflowId={WorkflowId}, runId={RunId})",
                execution.NamespaceId,
                execution.WorkflowId,
                execution.RunId);

            return Result.Fail<WorkflowExecution>(
                OdinErrors.WorkflowAlreadyExists(execution.WorkflowId));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to create workflow execution {WorkflowId} ({RunId})",
                execution.WorkflowId,
                execution.RunId);

            return Result.Fail<WorkflowExecution>(
                Error.From($"Database error: {ex.Message}", OdinErrorCodes.PersistenceError));
        }
    }

    public async Task<Result<WorkflowExecution>> GetAsync(
        string namespaceId,
        string workflowId,
        string runId,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseIdentifiers(namespaceId, workflowId, runId, out var nsGuid, out var runGuid, out var error))
        {
            return Result.Fail<WorkflowExecution>(error!);
        }

        var connectionResult = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        if (connectionResult.IsFailure)
        {
            return Result.Fail<WorkflowExecution>(connectionResult.Error!);
        }

        using var connection = connectionResult.Value;

        const string sql = @"
SELECT *
FROM workflow_executions
WHERE namespace_id = @NamespaceId
  AND workflow_id = @WorkflowId
  AND run_id = @RunId
LIMIT 1";

        try
        {
            var row = await connection.QuerySingleOrDefaultAsync<WorkflowExecutionRow>(sql, new
            {
                NamespaceId = nsGuid,
                WorkflowId = workflowId,
                RunId = runGuid
            });

            if (row is null)
            {
                return Result.Fail<WorkflowExecution>(
                    OdinErrors.WorkflowNotFound(workflowId, runId));
            }

            return Result.Ok(row.ToModel());
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to get workflow execution {WorkflowId} ({RunId})",
                workflowId,
                runId);

            return Result.Fail<WorkflowExecution>(
                Error.From($"Database error: {ex.Message}", OdinErrorCodes.PersistenceError));
        }
    }

    public async Task<Result<WorkflowExecution>> GetCurrentAsync(
        string namespaceId,
        string workflowId,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(namespaceId, out var nsGuid))
        {
            return Result.Fail<WorkflowExecution>(
                OdinErrors.WorkflowNotFound(workflowId));
        }

        var connectionResult = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        if (connectionResult.IsFailure)
        {
            return Result.Fail<WorkflowExecution>(connectionResult.Error!);
        }

        using var connection = connectionResult.Value;

        const string sql = @"
SELECT *
FROM workflow_executions
WHERE namespace_id = @NamespaceId
  AND workflow_id = @WorkflowId
ORDER BY started_at DESC
LIMIT 1";

        try
        {
            var row = await connection.QuerySingleOrDefaultAsync<WorkflowExecutionRow>(sql, new
            {
                NamespaceId = nsGuid,
                WorkflowId = workflowId
            });

            if (row is null)
            {
                return Result.Fail<WorkflowExecution>(
                    OdinErrors.WorkflowNotFound(workflowId));
            }

            return Result.Ok(row.ToModel());
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to get current workflow execution for {WorkflowId}",
                workflowId);

            return Result.Fail<WorkflowExecution>(
                Error.From($"Database error: {ex.Message}", OdinErrorCodes.PersistenceError));
        }
    }

    public async Task<Result<WorkflowExecution>> UpdateAsync(
        WorkflowExecution execution,
        int expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentOutOfRangeException.ThrowIfLessThan(expectedVersion, 0);

        var connectionResult = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        if (connectionResult.IsFailure)
        {
            return Result.Fail<WorkflowExecution>(connectionResult.Error!);
        }

        using var connection = connectionResult.Value;
        using var transaction = connection.BeginTransaction();

        try
        {
            var existing = await connection.QuerySingleOrDefaultAsync<WorkflowExecutionRow>(
                @"SELECT * FROM workflow_executions
                  WHERE namespace_id = @NamespaceId
                    AND workflow_id = @WorkflowId
                    AND run_id = @RunId
                  FOR UPDATE",
                new
                {
                    execution.NamespaceId,
                    execution.WorkflowId,
                    execution.RunId
                },
                transaction);

            if (existing is null)
            {
                transaction.Rollback();
                return Result.Fail<WorkflowExecution>(
                    OdinErrors.WorkflowNotFound(execution.WorkflowId, execution.RunId.ToString()));
            }

            if (existing.Version != expectedVersion)
            {
                transaction.Rollback();
                return Result.Fail<WorkflowExecution>(
                    OdinErrors.ConcurrencyConflict(
                        "workflowExecution",
                        expectedVersion,
                        existing.Version));
            }

            var normalized = NormalizeForUpdate(execution, existing.ToModel(), expectedVersion);

            var updateSql = @"
UPDATE workflow_executions
SET
    workflow_type = @WorkflowType,
    task_queue = @TaskQueue,
    workflow_state = @WorkflowState,
    execution_state = @ExecutionState,
    next_event_id = @NextEventId,
    last_processed_event_id = @LastProcessedEventId,
    workflow_timeout_seconds = @WorkflowTimeoutSeconds,
    run_timeout_seconds = @RunTimeoutSeconds,
    task_timeout_seconds = @TaskTimeoutSeconds,
    retry_policy = @RetryPolicy,
    cron_schedule = @CronSchedule,
    parent_workflow_id = @ParentWorkflowId,
    parent_run_id = @ParentRunId,
    initiated_id = @InitiatedId,
    completion_event_id = @CompletionEventId,
    memo = @Memo,
    search_attributes = @SearchAttributes,
    auto_reset_points = @AutoResetPoints,
    started_at = @StartedAt,
    completed_at = @CompletedAt,
    last_updated_at = @LastUpdatedAt,
    shard_id = @ShardId,
    version = @Version
WHERE namespace_id = @NamespaceId
  AND workflow_id = @WorkflowId
  AND run_id = @RunId
";

            await connection.ExecuteAsync(
                updateSql,
                new
                {
                    normalized.NamespaceId,
                    normalized.WorkflowId,
                    normalized.RunId,
                    normalized.WorkflowType,
                    normalized.TaskQueue,
                    WorkflowState = ToDatabaseState(normalized.WorkflowState),
                    ExecutionState = normalized.ExecutionState,
                    normalized.NextEventId,
                    normalized.LastProcessedEventId,
                    normalized.WorkflowTimeoutSeconds,
                    normalized.RunTimeoutSeconds,
                    normalized.TaskTimeoutSeconds,
                    RetryPolicy = ToJson(normalized.RetryPolicy),
                    normalized.CronSchedule,
                    normalized.ParentWorkflowId,
                    normalized.ParentRunId,
                    normalized.InitiatedId,
                    normalized.CompletionEventId,
                    Memo = ToJson(normalized.Memo),
                    SearchAttributes = ToJson(normalized.SearchAttributes),
                    AutoResetPoints = ToJson(normalized.AutoResetPoints),
                    normalized.StartedAt,
                    normalized.CompletedAt,
                    normalized.LastUpdatedAt,
                    normalized.ShardId,
                    normalized.Version
                },
                transaction);

            var refreshed = await connection.QuerySingleAsync<WorkflowExecutionRow>(
                @"SELECT * FROM workflow_executions
                  WHERE namespace_id = @NamespaceId
                    AND workflow_id = @WorkflowId
                    AND run_id = @RunId",
                new
                {
                    normalized.NamespaceId,
                    normalized.WorkflowId,
                    normalized.RunId
                },
                transaction);

            transaction.Commit();
            return Result.Ok(refreshed.ToModel());
        }
        catch (Exception ex)
        {
            transaction.Rollback();

            _logger.LogError(
                ex,
                "Failed to update workflow execution {WorkflowId} ({RunId})",
                execution.WorkflowId,
                execution.RunId);

            return Result.Fail<WorkflowExecution>(
                Error.From($"Database error: {ex.Message}", OdinErrorCodes.PersistenceError));
        }
    }

    public Task<Result<WorkflowExecution>> UpdateWithEventIdAsync(
        WorkflowExecution execution,
        int expectedVersion,
        long nextEventId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution);

        var updatedExecution = execution with { NextEventId = nextEventId };
        return UpdateAsync(updatedExecution, expectedVersion, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<WorkflowExecutionInfo>>> ListAsync(
        string namespaceId,
        WorkflowState? state = null,
        int pageSize = 100,
        string? pageToken = null,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(namespaceId, out var nsGuid))
        {
            return Result.Fail<IReadOnlyList<WorkflowExecutionInfo>>(
                OdinErrors.WorkflowNotFound(namespaceId));
        }

        var normalizedPageSize = pageSize <= 0 ? 100 : pageSize;
        var offset = 0;

        if (!string.IsNullOrWhiteSpace(pageToken) &&
            int.TryParse(pageToken, out var parsedOffset) &&
            parsedOffset >= 0)
        {
            offset = parsedOffset;
        }

        var connectionResult = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        if (connectionResult.IsFailure)
        {
            return Result.Fail<IReadOnlyList<WorkflowExecutionInfo>>(connectionResult.Error!);
        }

        using var connection = connectionResult.Value;

        var sql = @"
SELECT
    workflow_id,
    run_id,
    workflow_type,
    task_queue,
    workflow_state,
    started_at,
    completed_at,
    next_event_id,
    parent_workflow_id,
    parent_run_id
FROM workflow_executions
WHERE namespace_id = @NamespaceId
";

        if (state is not null)
        {
            sql += "  AND workflow_state = @WorkflowState\n";
        }

        sql += @"
ORDER BY started_at DESC
LIMIT @PageSize OFFSET @Offset
";

        try
        {
            var results = await connection.QueryAsync<WorkflowExecutionProjection>(sql, new
            {
                NamespaceId = nsGuid,
                WorkflowState = state is null ? null : ToDatabaseState(state.Value),
                PageSize = normalizedPageSize,
                Offset = offset
            });

            var infos = results
                .Select(row => MapToExecutionInfo(
                    row.WorkflowState,
                    row.WorkflowId,
                    row.RunId,
                    row.WorkflowType,
                    row.TaskQueue,
                    row.StartedAt,
                    row.CompletedAt,
                    row.NextEventId,
                    row.ParentWorkflowId,
                    row.ParentRunId))
                .ToList();

            return Result.Ok<IReadOnlyList<WorkflowExecutionInfo>>(infos);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to list workflow executions for namespace {NamespaceId}",
                namespaceId);

            return Result.Fail<IReadOnlyList<WorkflowExecutionInfo>>(
                Error.From($"Database error: {ex.Message}", OdinErrorCodes.PersistenceError));
        }
    }

    public int CalculateShardId(string workflowId, int shardCount = 512)
        => HashingUtilities.CalculateShardId(workflowId, shardCount);

    public async Task<Result<Unit>> TerminateAsync(
        string namespaceId,
        string workflowId,
        string runId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseIdentifiers(namespaceId, workflowId, runId, out var nsGuid, out var runGuid, out var error))
        {
            return Result.Fail<Unit>(error!);
        }

        var connectionResult = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        if (connectionResult.IsFailure)
        {
            return Result.Fail<Unit>(connectionResult.Error!);
        }

        using var connection = connectionResult.Value;

        const string sql = @"
UPDATE workflow_executions
SET workflow_state = 'terminated',
    completed_at = COALESCE(completed_at, NOW()),
    completion_event_id = COALESCE(completion_event_id, next_event_id),
    last_updated_at = NOW()
WHERE namespace_id = @NamespaceId
  AND workflow_id = @WorkflowId
  AND run_id = @RunId
";

        try
        {
            var rows = await connection.ExecuteAsync(sql, new
            {
                NamespaceId = nsGuid,
                WorkflowId = workflowId,
                RunId = runGuid
            });

            if (rows == 0)
            {
                return Result.Fail<Unit>(
                    OdinErrors.WorkflowNotFound(workflowId, runId));
            }

            return Result.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to terminate workflow execution {WorkflowId} ({RunId})",
                workflowId,
                runId);

            return Result.Fail<Unit>(
                Error.From($"Database error: {ex.Message}", OdinErrorCodes.PersistenceError));
        }
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

    private static WorkflowExecution NormalizeForUpdate(
        WorkflowExecution incoming,
        WorkflowExecution existing,
        int expectedVersion)
    {
        var now = DateTimeOffset.UtcNow;

        var startedAt = incoming.StartedAt == default
            ? existing.StartedAt
            : incoming.StartedAt;

        var shardId = incoming.ShardId != 0
            ? incoming.ShardId
            : existing.ShardId != 0
                ? existing.ShardId
                : HashingUtilities.CalculateShardId(incoming.WorkflowId);

        var completedAt = incoming.CompletedAt ??
                          (IsTerminal(incoming.WorkflowState)
                              ? now
                              : existing.CompletedAt);

        var completionEventId = incoming.CompletionEventId ??
                                existing.CompletionEventId;

        return incoming with
        {
            StartedAt = startedAt,
            CompletedAt = completedAt,
            LastUpdatedAt = now,
            ShardId = shardId,
            Version = expectedVersion + 1,
            CompletionEventId = completionEventId
        };
    }

    private static bool IsTerminal(WorkflowState state) => state is WorkflowState.Completed
        or WorkflowState.Failed
        or WorkflowState.Canceled
        or WorkflowState.Terminated
        or WorkflowState.TimedOut;

    private static string? ToJson(JsonDocument? document)
        => document?.RootElement.GetRawText();

    private static JsonDocument? ParseJson(string? json)
        => string.IsNullOrWhiteSpace(json) ? null : JsonDocument.Parse(json);

    private static WorkflowExecutionInfo MapToExecutionInfo(
        string workflowState,
        string workflowId,
        Guid runId,
        string workflowType,
        string taskQueue,
        DateTimeOffset startedAt,
        DateTimeOffset? completedAt,
        long nextEventId,
        string? parentWorkflowId,
        Guid? parentRunId)
    {
        var status = MapWorkflowStatus(workflowState);
        var duration = completedAt.HasValue ? completedAt - startedAt : (TimeSpan?)null;

        ParentExecutionInfo? parent = null;
        if (!string.IsNullOrWhiteSpace(parentWorkflowId) && parentRunId.HasValue)
        {
            parent = new ParentExecutionInfo
            {
                Namespace = string.Empty,
                WorkflowId = parentWorkflowId,
                RunId = parentRunId.Value.ToString()
            };
        }

        return new WorkflowExecutionInfo
        {
            WorkflowId = workflowId,
            RunId = runId.ToString(),
            WorkflowType = workflowType,
            TaskQueue = taskQueue,
            Status = status,
            StartTime = startedAt,
            CloseTime = completedAt,
            ExecutionDuration = duration,
            HistoryLength = Math.Max(0, nextEventId - 1),
            ParentExecution = parent,
            SearchAttributes = null,
            Memo = null
        };
    }

    private static ContractsWorkflowStatus MapWorkflowStatus(string workflowState)
        => FromDatabaseState(workflowState) switch
        {
            WorkflowState.Running => ContractsWorkflowStatus.Running,
            WorkflowState.Completed => ContractsWorkflowStatus.Completed,
            WorkflowState.Failed => ContractsWorkflowStatus.Failed,
            WorkflowState.Canceled => ContractsWorkflowStatus.Canceled,
            WorkflowState.Terminated => ContractsWorkflowStatus.Terminated,
            WorkflowState.ContinuedAsNew => ContractsWorkflowStatus.ContinuedAsNew,
            WorkflowState.TimedOut => ContractsWorkflowStatus.TimedOut,
            _ => ContractsWorkflowStatus.Unspecified
        };

    private static string ToDatabaseState(WorkflowState state)
        => state switch
        {
            WorkflowState.Running => "running",
            WorkflowState.Completed => "completed",
            WorkflowState.Failed => "failed",
            WorkflowState.Canceled => "canceled",
            WorkflowState.Terminated => "terminated",
            WorkflowState.ContinuedAsNew => "continued_as_new",
            WorkflowState.TimedOut => "timed_out",
            _ => "running"
        };

    private static WorkflowState FromDatabaseState(string state)
        => state switch
        {
            "running" => WorkflowState.Running,
            "completed" => WorkflowState.Completed,
            "failed" => WorkflowState.Failed,
            "canceled" => WorkflowState.Canceled,
            "terminated" => WorkflowState.Terminated,
            "continued_as_new" => WorkflowState.ContinuedAsNew,
            "timed_out" => WorkflowState.TimedOut,
            _ => WorkflowState.Running
        };

    private sealed class WorkflowExecutionProjection
    {
        public string WorkflowId { get; set; } = string.Empty;
        public Guid RunId { get; set; }
        public string WorkflowType { get; set; } = string.Empty;
        public string TaskQueue { get; set; } = string.Empty;
        public string WorkflowState { get; set; } = string.Empty;
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        public long NextEventId { get; set; }
        public string? ParentWorkflowId { get; set; }
        public Guid? ParentRunId { get; set; }
    }

    private sealed class WorkflowExecutionRow
    {
        public Guid NamespaceId { get; set; }
        public string WorkflowId { get; set; } = string.Empty;
        public Guid RunId { get; set; }
        public string WorkflowType { get; set; } = string.Empty;
        public string TaskQueue { get; set; } = string.Empty;
        public string WorkflowState { get; set; } = string.Empty;
        public byte[]? ExecutionState { get; set; }
        public long NextEventId { get; set; }
        public long LastProcessedEventId { get; set; }
        public int? WorkflowTimeoutSeconds { get; set; }
        public int? RunTimeoutSeconds { get; set; }
        public int? TaskTimeoutSeconds { get; set; }
        public string? RetryPolicy { get; set; }
        public string? CronSchedule { get; set; }
        public string? ParentWorkflowId { get; set; }
        public Guid? ParentRunId { get; set; }
        public long? InitiatedId { get; set; }
        public long? CompletionEventId { get; set; }
        public string? Memo { get; set; }
        public string? SearchAttributes { get; set; }
        public string? AutoResetPoints { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        public DateTimeOffset LastUpdatedAt { get; set; }
        public int ShardId { get; set; }
        public long Version { get; set; }

        public WorkflowExecution ToModel()
        {
            return new WorkflowExecution
            {
                NamespaceId = NamespaceId,
                WorkflowId = WorkflowId,
                RunId = RunId,
                WorkflowType = WorkflowType,
                TaskQueue = TaskQueue,
                WorkflowState = FromDatabaseState(WorkflowState),
                ExecutionState = ExecutionState,
                NextEventId = NextEventId,
                LastProcessedEventId = LastProcessedEventId,
                WorkflowTimeoutSeconds = WorkflowTimeoutSeconds,
                RunTimeoutSeconds = RunTimeoutSeconds,
                TaskTimeoutSeconds = TaskTimeoutSeconds,
                RetryPolicy = ParseJson(RetryPolicy),
                CronSchedule = CronSchedule,
                ParentWorkflowId = ParentWorkflowId,
                ParentRunId = ParentRunId,
                InitiatedId = InitiatedId,
                CompletionEventId = CompletionEventId,
                Memo = ParseJson(Memo),
                SearchAttributes = ParseJson(SearchAttributes),
                AutoResetPoints = ParseJson(AutoResetPoints),
                StartedAt = StartedAt,
                CompletedAt = CompletedAt,
                LastUpdatedAt = LastUpdatedAt,
                ShardId = ShardId,
                Version = Version
            };
        }
    }
}
