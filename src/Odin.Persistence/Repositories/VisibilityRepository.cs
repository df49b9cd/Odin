using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Hugo;
using Microsoft.Extensions.Logging;
using Npgsql;
using Odin.Contracts;
using Odin.Core;
using Odin.Persistence.Interfaces;
using static Hugo.Go;
using ContractsWorkflowStatus = Odin.Contracts.WorkflowStatus;

namespace Odin.Persistence.Repositories;

/// <summary>
/// PostgreSQL implementation of workflow visibility persistence.
/// </summary>
public sealed class VisibilityRepository(
    IDbConnectionFactory connectionFactory,
    ILogger<VisibilityRepository> logger) : IVisibilityRepository
{
    private const int DefaultPageSize = 100;
    private const int MaxPageSize = 500;

    private readonly IDbConnectionFactory _connectionFactory = connectionFactory;
    private readonly ILogger<VisibilityRepository> _logger = logger;

    public async Task<Result<Unit>> UpsertAsync(
        string namespaceId,
        string workflowId,
        string runId,
        WorkflowExecutionInfo info,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(info);

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
INSERT INTO visibility_records (
    namespace_id,
    workflow_id,
    run_id,
    workflow_type,
    task_queue,
    workflow_state,
    start_time,
    execution_time,
    close_time,
    status,
    history_length,
    execution_duration_ms,
    state_transition_count,
    memo,
    search_attributes,
    parent_workflow_id,
    parent_run_id,
    created_at,
    updated_at
) VALUES (
    @NamespaceId,
    @WorkflowId,
    @RunId,
    @WorkflowType,
    @TaskQueue,
    @WorkflowState,
    @StartTime,
    @ExecutionTime,
    @CloseTime,
    @Status,
    @HistoryLength,
    @ExecutionDurationMs,
    @StateTransitionCount,
    CAST(@Memo AS JSONB),
    CAST(@SearchAttributes AS JSONB),
    @ParentWorkflowId,
    @ParentRunId,
    NOW(),
    NOW()
)
ON CONFLICT (namespace_id, workflow_id, run_id)
DO UPDATE SET
    workflow_type = EXCLUDED.workflow_type,
    task_queue = EXCLUDED.task_queue,
    workflow_state = EXCLUDED.workflow_state,
    start_time = EXCLUDED.start_time,
    execution_time = EXCLUDED.execution_time,
    close_time = EXCLUDED.close_time,
    status = EXCLUDED.status,
    history_length = EXCLUDED.history_length,
    execution_duration_ms = EXCLUDED.execution_duration_ms,
    state_transition_count = EXCLUDED.state_transition_count,
    memo = EXCLUDED.memo,
    search_attributes = EXCLUDED.search_attributes,
    parent_workflow_id = EXCLUDED.parent_workflow_id,
    parent_run_id = EXCLUDED.parent_run_id,
    updated_at = NOW();";

        var parameters = new
        {
            NamespaceId = nsGuid,
            WorkflowId = workflowId,
            RunId = runGuid,
            info.WorkflowType,
            info.TaskQueue,
            WorkflowState = info.Status.ToString(),
            StartTime = info.StartTime.UtcDateTime,
            ExecutionTime = info.StartTime.UtcDateTime,
            CloseTime = info.CloseTime?.UtcDateTime,
            Status = info.Status.ToString(),
            HistoryLength = info.HistoryLength,
            ExecutionDurationMs = info.ExecutionDuration.HasValue
                ? (long?)Math.Round(info.ExecutionDuration.Value.TotalMilliseconds)
                : null,
            StateTransitionCount = 0,
            Memo = ToJson(info.Memo),
            SearchAttributes = ToJson(info.SearchAttributes),
            ParentWorkflowId = info.ParentExecution?.WorkflowId,
            ParentRunId = ParseGuid(info.ParentExecution?.RunId)
        };

        try
        {
            await connection.ExecuteAsync(sql, parameters);
            return Result.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to upsert visibility record for workflow {WorkflowId}/{RunId}",
                workflowId,
                runId);

            return Result.Fail<Unit>(
                Error.From($"Failed to upsert visibility record: {ex.Message}", OdinErrorCodes.PersistenceError));
        }
    }

    public async Task<Result<ListWorkflowExecutionsResponse>> ListAsync(
        ListWorkflowExecutionsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Namespace);

        var pageSize = NormalizePageSize(request.PageSize);
        var offsetResult = DecodeOffset(request.NextPageToken);
        if (offsetResult.IsFailure)
        {
            return Result.Fail<ListWorkflowExecutionsResponse>(offsetResult.Error!);
        }

        return await QueryVisibilityAsync(
            request.Namespace,
            request.Query,
            pageSize,
            offsetResult.Value,
            cancellationToken);
    }

    public async Task<Result<ListWorkflowExecutionsResponse>> SearchAsync(
        string namespaceId,
        string query,
        int pageSize = DefaultPageSize,
        string? pageToken = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceId);

        var normalizedQuery = string.IsNullOrWhiteSpace(query) ? null : query;
        var size = NormalizePageSize(pageSize);
        var offsetResult = DecodeOffset(pageToken);
        if (offsetResult.IsFailure)
        {
            return Result.Fail<ListWorkflowExecutionsResponse>(offsetResult.Error!);
        }

        return await QueryVisibilityAsync(
            namespaceId,
            normalizedQuery,
            size,
            offsetResult.Value,
            cancellationToken);
    }

    public async Task<Result<long>> CountAsync(
        string namespaceId,
        string? query = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceId);

        if (!TryParseNamespace(namespaceId, out var nsGuid, out var error))
        {
            return Result.Fail<long>(error!);
        }

        var connectionResult = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        if (connectionResult.IsFailure)
        {
            return Result.Fail<long>(connectionResult.Error!);
        }

        using var connection = connectionResult.Value;

        var filter = BuildQueryFilter(query, out var filterParameters);
        filterParameters.Add("NamespaceId", nsGuid);

        var sql = $@"
SELECT COUNT(*)::BIGINT
FROM visibility_records
WHERE namespace_id = @NamespaceId
{filter}";

        try
        {
            var count = await connection.ExecuteScalarAsync<long>(sql, filterParameters);
            return Result.Ok(count);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to count visibility records for namespace {NamespaceId}",
                namespaceId);

            return Result.Fail<long>(
                Error.From($"Failed to count visibility records: {ex.Message}", OdinErrorCodes.PersistenceError));
        }
    }

    public async Task<Result<Unit>> UpdateTagsAsync(
        string namespaceId,
        string workflowId,
        string runId,
        IReadOnlyList<string> tags,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        if (!TryParseIdentifiers(namespaceId, workflowId, runId, out var nsGuid, out var runGuid, out var error))
        {
            return Result.Fail<Unit>(error!);
        }

        var normalizedTags = tags?
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? Array.Empty<string>();

        var connectionResult = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        if (connectionResult.IsFailure)
        {
            return Result.Fail<Unit>(connectionResult.Error!);
        }

        using var connection = connectionResult.Value;
        using var transaction = connection.BeginTransaction();

        try
        {
            await connection.ExecuteAsync(
                @"DELETE FROM workflow_tags
                  WHERE namespace_id = @NamespaceId
                    AND workflow_id = @WorkflowId
                    AND run_id = @RunId",
                new { NamespaceId = nsGuid, WorkflowId = workflowId, RunId = runGuid },
                transaction);

            if (normalizedTags.Length > 0)
            {
                const string insertSql = @"
INSERT INTO workflow_tags (
    namespace_id,
    workflow_id,
    run_id,
    tag_key,
    tag_value
) VALUES (
    @NamespaceId,
    @WorkflowId,
    @RunId,
    @TagKey,
    @TagValue
)";

                foreach (var tag in normalizedTags)
                {
                    await connection.ExecuteAsync(
                        insertSql,
                        new
                        {
                            NamespaceId = nsGuid,
                            WorkflowId = workflowId,
                            RunId = runGuid,
                            TagKey = tag,
                            TagValue = (string?)null
                        },
                        transaction);
                }
            }

            transaction.Commit();
            return Result.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            transaction.Rollback();

            _logger.LogError(
                ex,
                "Failed to update tags for workflow {WorkflowId}/{RunId}",
                workflowId,
                runId);

            return Result.Fail<Unit>(
                Error.From($"Failed to update workflow tags: {ex.Message}", OdinErrorCodes.PersistenceError));
        }
    }

    public async Task<Result<ListWorkflowExecutionsResponse>> SearchByTagsAsync(
        string namespaceId,
        IReadOnlyList<string> tags,
        bool matchAll = false,
        int pageSize = DefaultPageSize,
        string? pageToken = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceId);

        if (tags is null || tags.Count == 0)
        {
            return Result.Ok(new ListWorkflowExecutionsResponse
            {
                Executions = Array.Empty<WorkflowExecutionInfo>(),
                NextPageToken = null
            });
        }

        var normalizedTags = tags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedTags.Length == 0)
        {
            return Result.Ok(new ListWorkflowExecutionsResponse
            {
                Executions = Array.Empty<WorkflowExecutionInfo>(),
                NextPageToken = null
            });
        }

        var size = NormalizePageSize(pageSize);
        var offsetResult = DecodeOffset(pageToken);
        if (offsetResult.IsFailure)
        {
            return Result.Fail<ListWorkflowExecutionsResponse>(offsetResult.Error!);
        }

        if (!TryParseNamespace(namespaceId, out var nsGuid, out var error))
        {
            return Result.Fail<ListWorkflowExecutionsResponse>(error!);
        }

        var connectionResult = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        if (connectionResult.IsFailure)
        {
            return Result.Fail<ListWorkflowExecutionsResponse>(connectionResult.Error!);
        }

        using var connection = connectionResult.Value;

        var parameters = new DynamicParameters();
        parameters.Add("NamespaceId", nsGuid);
        parameters.Add("Tags", normalizedTags);
        parameters.Add("Offset", offsetResult.Value);
        parameters.Add("Limit", size + 1);
        parameters.Add("TagCount", normalizedTags.Length);

        var matchAllClause = matchAll
            ? "AND (SELECT COUNT(DISTINCT wt.tag_key) FROM workflow_tags wt WHERE wt.namespace_id = vr.namespace_id AND wt.workflow_id = vr.workflow_id AND wt.run_id = vr.run_id AND wt.tag_key = ANY(@Tags)) = @TagCount"
            : "AND EXISTS (SELECT 1 FROM workflow_tags wt WHERE wt.namespace_id = vr.namespace_id AND wt.workflow_id = vr.workflow_id AND wt.run_id = vr.run_id AND wt.tag_key = ANY(@Tags))";

        var sql = $@"
SELECT
    vr.namespace_id,
    vr.workflow_id,
    vr.run_id,
    vr.workflow_type,
    vr.task_queue,
    vr.workflow_state,
    vr.start_time,
    vr.execution_time,
    vr.close_time,
    vr.status,
    vr.history_length,
    vr.execution_duration_ms,
    vr.memo,
    vr.search_attributes,
    vr.parent_workflow_id,
    vr.parent_run_id
FROM visibility_records vr
WHERE vr.namespace_id = @NamespaceId
{matchAllClause}
ORDER BY vr.start_time DESC, vr.workflow_id, vr.run_id
LIMIT @Limit OFFSET @Offset";

        try
        {
            var rows = (await connection.QueryAsync<VisibilityRow>(sql, parameters)).ToList();
            return Result.Ok(ToResponse(rows, size, offsetResult.Value));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to search visibility records by tags for namespace {NamespaceId}",
                namespaceId);

            return Result.Fail<ListWorkflowExecutionsResponse>(
                Error.From($"Failed to search by tags: {ex.Message}", OdinErrorCodes.PersistenceError));
        }
    }

    public async Task<Result<int>> ArchiveOldRecordsAsync(
        string namespaceId,
        DateTimeOffset olderThan,
        int batchSize = 1000,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceId);
        batchSize = Math.Clamp(batchSize, 1, 5000);

        if (!TryParseNamespace(namespaceId, out var nsGuid, out var error))
        {
            return Result.Fail<int>(error!);
        }

        var connectionResult = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        if (connectionResult.IsFailure)
        {
            return Result.Fail<int>(connectionResult.Error!);
        }

        using var connection = connectionResult.Value;

        const string sql = @"
WITH candidates AS (
    SELECT namespace_id, workflow_id, run_id
    FROM visibility_records
    WHERE namespace_id = @NamespaceId
      AND close_time IS NOT NULL
      AND close_time < @OlderThan
    ORDER BY close_time
    LIMIT @BatchSize
),
deleted_tags AS (
    DELETE FROM workflow_tags wt
    USING candidates c
    WHERE wt.namespace_id = c.namespace_id
      AND wt.workflow_id = c.workflow_id
      AND wt.run_id = c.run_id
    RETURNING 1
),
deleted_visibility AS (
    DELETE FROM visibility_records vr
    USING candidates c
    WHERE vr.namespace_id = c.namespace_id
      AND vr.workflow_id = c.workflow_id
      AND vr.run_id = c.run_id
    RETURNING 1
)
SELECT COUNT(*) FROM deleted_visibility;";

        try
        {
            var removed = await connection.ExecuteScalarAsync<int>(
                sql,
                new
                {
                    NamespaceId = nsGuid,
                    OlderThan = olderThan.UtcDateTime,
                    BatchSize = batchSize
                });

            return Result.Ok(removed);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to archive visibility records for namespace {NamespaceId}",
                namespaceId);

            return Result.Fail<int>(
                Error.From($"Failed to archive visibility records: {ex.Message}", OdinErrorCodes.PersistenceError));
        }
    }

    public async Task<Result<Unit>> DeleteAsync(
        string namespaceId,
        string workflowId,
        string runId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

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
        using var transaction = connection.BeginTransaction();

        try
        {
            await connection.ExecuteAsync(
                @"DELETE FROM workflow_tags
                  WHERE namespace_id = @NamespaceId
                    AND workflow_id = @WorkflowId
                    AND run_id = @RunId",
                new { NamespaceId = nsGuid, WorkflowId = workflowId, RunId = runGuid },
                transaction);

            await connection.ExecuteAsync(
                @"DELETE FROM visibility_records
                  WHERE namespace_id = @NamespaceId
                    AND workflow_id = @WorkflowId
                    AND run_id = @RunId",
                new { NamespaceId = nsGuid, workflowId, RunId = runGuid },
                transaction);

            transaction.Commit();
            return Result.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            transaction.Rollback();

            _logger.LogError(
                ex,
                "Failed to delete visibility record for workflow {WorkflowId}/{RunId}",
                workflowId,
                runId);

            return Result.Fail<Unit>(
                Error.From($"Failed to delete visibility record: {ex.Message}", OdinErrorCodes.PersistenceError));
        }
    }

    private async Task<Result<ListWorkflowExecutionsResponse>> QueryVisibilityAsync(
        string namespaceId,
        string? query,
        int pageSize,
        int offset,
        CancellationToken cancellationToken)
    {
        if (!TryParseNamespace(namespaceId, out var nsGuid, out var error))
        {
            return Result.Fail<ListWorkflowExecutionsResponse>(error!);
        }

        var connectionResult = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        if (connectionResult.IsFailure)
        {
            return Result.Fail<ListWorkflowExecutionsResponse>(connectionResult.Error!);
        }

        using var connection = connectionResult.Value;

        var parameters = new DynamicParameters();
        parameters.Add("NamespaceId", nsGuid);
        parameters.Add("Offset", offset);
        parameters.Add("Limit", pageSize + 1);

        var filterClause = BuildQueryFilter(query, out var queryParameters);
        parameters.AddDynamicParams(queryParameters);

        var sql = $@"
SELECT
    namespace_id,
    workflow_id,
    run_id,
    workflow_type,
    task_queue,
    workflow_state,
    start_time,
    execution_time,
    close_time,
    status,
    history_length,
    execution_duration_ms,
    memo,
    search_attributes,
    parent_workflow_id,
    parent_run_id
FROM visibility_records
WHERE namespace_id = @NamespaceId
{filterClause}
ORDER BY start_time DESC, workflow_id, run_id
LIMIT @Limit OFFSET @Offset";

        try
        {
            var rows = (await connection.QueryAsync<VisibilityRow>(sql, parameters)).ToList();
            return Result.Ok(ToResponse(rows, pageSize, offset));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to query visibility records for namespace {NamespaceId}",
                namespaceId);

            return Result.Fail<ListWorkflowExecutionsResponse>(
                Error.From($"Failed to query visibility records: {ex.Message}", OdinErrorCodes.PersistenceError));
        }
    }

    private static ListWorkflowExecutionsResponse ToResponse(
        List<VisibilityRow> rows,
        int pageSize,
        int offset)
    {
        var hasMore = rows.Count > pageSize;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        var executions = rows
            .Select(MapToExecutionInfo)
            .ToList();

        var nextToken = hasMore
            ? EncodeOffset(offset + executions.Count)
            : null;

        return new ListWorkflowExecutionsResponse
        {
            Executions = executions,
            NextPageToken = nextToken
        };
    }

    private static WorkflowExecutionInfo MapToExecutionInfo(VisibilityRow row)
    {
        var status = ParseStatus(row.Status) ?? ContractsWorkflowStatus.Unspecified;
        var duration = row.ExecutionDurationMs.HasValue
            ? TimeSpan.FromMilliseconds(row.ExecutionDurationMs.Value)
            : (TimeSpan?)null;

        ParentExecutionInfo? parent = null;
        if (!string.IsNullOrWhiteSpace(row.ParentWorkflowId) && row.ParentRunId.HasValue)
        {
            parent = new ParentExecutionInfo
            {
                Namespace = string.Empty,
                WorkflowId = row.ParentWorkflowId,
                RunId = row.ParentRunId.Value.ToString()
            };
        }

        var start = ToUtcDateTimeOffset(row.StartTime);
        var close = row.CloseTime.HasValue ? ToUtcDateTimeOffset(row.CloseTime.Value) : (DateTimeOffset?)null;

        return new WorkflowExecutionInfo
        {
            WorkflowId = row.WorkflowId,
            RunId = row.RunId.ToString(),
            WorkflowType = row.WorkflowType,
            TaskQueue = row.TaskQueue,
            Status = status,
            StartTime = start,
            CloseTime = close,
            ExecutionDuration = duration,
            HistoryLength = row.HistoryLength,
            ParentExecution = parent,
            Memo = ParseDictionary(row.Memo),
            SearchAttributes = ParseDictionary(row.SearchAttributes)
        };
    }

    private static DateTimeOffset ToUtcDateTimeOffset(DateTime value)
    {
        var specified = value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
        return new DateTimeOffset(specified);
    }

    private static string BuildQueryFilter(string? query, out DynamicParameters parameters)
    {
        parameters = new DynamicParameters();

        if (string.IsNullOrWhiteSpace(query))
        {
            return string.Empty;
        }

        var filters = new List<string>();

        var normalized = query.Trim();

        if (TryParseFieldExpression(normalized, out var fieldFilters, out var remaining))
        {
            foreach (var filter in fieldFilters)
            {
                filters.Add(filter.Sql);
                parameters.Add(filter.ParameterName, filter.Value);
            }

            if (string.IsNullOrWhiteSpace(remaining))
            {
                return $" AND {string.Join(" AND ", filters)}";
            }

            normalized = remaining;
        }

        var pattern = $"%{normalized}%";
        parameters.Add("SearchPattern", pattern);

        filters.Add("(workflow_id ILIKE @SearchPattern OR workflow_type ILIKE @SearchPattern OR status ILIKE @SearchPattern OR task_queue ILIKE @SearchPattern)");

        return $" AND {string.Join(" AND ", filters)}";
    }

    private static bool TryParseFieldExpression(
        string query,
        out List<FieldFilter> filters,
        out string? remaining)
    {
        filters = new List<FieldFilter>();
        remaining = query;

        if (string.IsNullOrWhiteSpace(query) || !query.Contains('='))
        {
            return false;
        }

        var expressions = query.Split("AND", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var builder = new StringBuilder();
        foreach (var expression in expressions)
        {
            var parts = expression.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                builder.Append(parts[0].Trim()).Append(' ');
                continue;
            }

            var field = parts[0].Trim();
            var value = parts[1].Trim().Trim('\'', '"');

            if (!TryMapField(field, out var column))
            {
                builder.Append(expression).Append(' ');
                continue;
            }

            var parameterName = $"Filter_{filters.Count}";
            filters.Add(new FieldFilter($"{column} = @{parameterName}", parameterName, value));
        }

        remaining = builder.ToString().Trim();
        return filters.Count > 0;
    }

    private static bool TryMapField(string field, out string columnName)
    {
        columnName = field switch
        {
            "WorkflowType" => "workflow_type",
            "WorkflowId" => "workflow_id",
            "Status" => "status",
            "TaskQueue" => "task_queue",
            "State" => "workflow_state",
            _ => string.Empty
        };

        return !string.IsNullOrEmpty(columnName);
    }

    private static ContractsWorkflowStatus? ParseStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Enum.TryParse<ContractsWorkflowStatus>(value, ignoreCase: true, out var status)
            ? status
            : null;
    }

    private static int NormalizePageSize(int? pageSize)
    {
        if (!pageSize.HasValue || pageSize.Value <= 0)
        {
            return DefaultPageSize;
        }

        return Math.Clamp(pageSize.Value, 1, MaxPageSize);
    }

    private static int NormalizePageSize(int pageSize)
        => NormalizePageSize((int?)pageSize);

    private static Result<int> DecodeOffset(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return Result.Ok(0);
        }

        try
        {
            var bytes = Convert.FromBase64String(token);
            var offsetString = Encoding.UTF8.GetString(bytes);
            return int.TryParse(offsetString, out var offset) && offset >= 0
                ? Result.Ok(offset)
                : Result.Fail<int>(Error.From("Invalid page token.", OdinErrorCodes.PersistenceError));
        }
        catch (FormatException)
        {
            return Result.Fail<int>(Error.From("Invalid page token.", OdinErrorCodes.PersistenceError));
        }
    }

    private static string? EncodeOffset(int offset)
    {
        if (offset <= 0)
        {
            return null;
        }

        var bytes = Encoding.UTF8.GetBytes(offset.ToString());
        return Convert.ToBase64String(bytes);
    }

    private static bool TryParseNamespace(string namespaceId, out Guid nsGuid, out Error? error)
    {
        if (Guid.TryParse(namespaceId, out nsGuid))
        {
            error = null;
            return true;
        }

        error = Error.From(
            $"Namespace identifier '{namespaceId}' is not a valid GUID.",
            OdinErrorCodes.PersistenceError);
        return false;
    }

    private static bool TryParseIdentifiers(
        string namespaceId,
        string workflowId,
        string runId,
        out Guid nsGuid,
        out Guid runGuid,
        out Error? error)
    {
        if (!Guid.TryParse(namespaceId, out nsGuid))
        {
            error = Error.From(
                $"Namespace identifier '{namespaceId}' is not a valid GUID.",
                OdinErrorCodes.PersistenceError);
            runGuid = Guid.Empty;
            return false;
        }

        if (!Guid.TryParse(runId, out runGuid))
        {
            error = Error.From(
                $"Run identifier '{runId}' is not a valid GUID.",
                OdinErrorCodes.PersistenceError);
            return false;
        }

        if (string.IsNullOrWhiteSpace(workflowId))
        {
            error = Error.From("Workflow identifier is required.", OdinErrorCodes.PersistenceError);
            return false;
        }

        error = null;
        return true;
    }

    private static Guid? ParseGuid(string? value)
        => Guid.TryParse(value, out var guid) ? guid : null;

    private static string? ToJson(Dictionary<string, object?>? value)
        => value is null or { Count: 0 } ? null : JsonSerializer.Serialize(value, JsonOptions.Default);

    private static Dictionary<string, object?>? ParseDictionary(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            result[property.Name] = ConvertJsonElement(property.Value);
        }

        return result;
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Undefined:
            case JsonValueKind.Null:
                return null;
            case JsonValueKind.String:
                if (element.TryGetDateTimeOffset(out var dto))
                {
                    return dto;
                }

                if (element.TryGetDateTime(out var dt))
                {
                    return dt;
                }

                if (element.TryGetGuid(out var guid))
                {
                    return guid.ToString();
                }

                return element.GetString();
            case JsonValueKind.Number:
                if (element.TryGetInt64(out var l))
                {
                    return l;
                }

                if (element.TryGetDecimal(out var dec))
                {
                    return dec;
                }

                if (element.TryGetDouble(out var dbl))
                {
                    return dbl;
                }

                return element.GetRawText();
            case JsonValueKind.True:
            case JsonValueKind.False:
                return element.GetBoolean();
            case JsonValueKind.Array:
                var list = new List<object?>();
                foreach (var item in element.EnumerateArray())
                {
                    list.Add(ConvertJsonElement(item));
                }

                return list;
            case JsonValueKind.Object:
                var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in element.EnumerateObject())
                {
                    dict[prop.Name] = ConvertJsonElement(prop.Value);
                }

                return dict;
            default:
                return element.GetRawText();
        }
    }

    private sealed record FieldFilter(string Sql, string ParameterName, object Value);

    private sealed record VisibilityRow(
        Guid NamespaceId,
        string WorkflowId,
        Guid RunId,
        string WorkflowType,
        string TaskQueue,
        string WorkflowState,
        DateTime StartTime,
        DateTime ExecutionTime,
        DateTime? CloseTime,
        string Status,
        long HistoryLength,
        long? ExecutionDurationMs,
        string? Memo,
        string? SearchAttributes,
        string? ParentWorkflowId,
        Guid? ParentRunId);
}
