using System.Collections.Concurrent;
using Hugo;
using Odin.Contracts;
using Odin.Core;
using Odin.Persistence.Interfaces;
using static Hugo.Go;
using CWorkflowExecution = Odin.Contracts.WorkflowExecution;
using CWorkflowStatus = Odin.Contracts.WorkflowStatus;

namespace Odin.Persistence.InMemory;

/// <summary>
/// In-memory repository for workflow execution state management.
/// </summary>
public sealed class InMemoryWorkflowExecutionRepository : IWorkflowExecutionRepository
{
    private readonly ConcurrentDictionary<(Guid NamespaceId, string WorkflowId, Guid RunId), CWorkflowExecution> _executions = new();
    private readonly ConcurrentDictionary<(Guid NamespaceId, string WorkflowId), Guid> _currentRuns = new();
    private readonly object _sync = new();

    public Task<Result<CWorkflowExecution>> CreateAsync(
        CWorkflowExecution execution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution);

        var key = (execution.NamespaceId, execution.WorkflowId, execution.RunId);

        lock (_sync)
        {
            if (_executions.ContainsKey(key))
            {
                return Task.FromResult(Result.Fail<CWorkflowExecution>(
                    OdinErrors.WorkflowAlreadyExists(execution.WorkflowId)));
            }

            var now = DateTimeOffset.UtcNow;
            var shardId = execution.ShardId != 0
                ? execution.ShardId
                : HashingUtilities.CalculateShardId(execution.WorkflowId);

            var created = execution with
            {
                StartedAt = execution.StartedAt == default ? now : execution.StartedAt,
                LastUpdatedAt = now,
                Version = execution.Version is 0 ? 1 : execution.Version,
                ShardId = shardId
            };

            _executions[key] = created;
            _currentRuns[(execution.NamespaceId, execution.WorkflowId)] = execution.RunId;

            return Task.FromResult(Result.Ok(created));
        }
    }

    public Task<Result<CWorkflowExecution>> GetAsync(
        string namespaceId,
        string workflowId,
        string runId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        if (!Guid.TryParse(namespaceId, out var nsId) ||
            !Guid.TryParse(runId, out var runGuid))
        {
            return Task.FromResult(Result.Fail<CWorkflowExecution>(
                OdinErrors.WorkflowNotFound(workflowId, runId)));
        }

        if (_executions.TryGetValue((nsId, workflowId, runGuid), out var execution))
        {
            return Task.FromResult(Result.Ok(execution));
        }

        return Task.FromResult(Result.Fail<CWorkflowExecution>(
            OdinErrors.WorkflowNotFound(workflowId, runId)));
    }

    public Task<Result<CWorkflowExecution>> GetCurrentAsync(
        string namespaceId,
        string workflowId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);

        if (!Guid.TryParse(namespaceId, out var nsId))
        {
            return Task.FromResult(Result.Fail<CWorkflowExecution>(
                OdinErrors.WorkflowNotFound(workflowId)));
        }

        if (_currentRuns.TryGetValue((nsId, workflowId), out var runId) &&
            _executions.TryGetValue((nsId, workflowId, runId), out var execution))
        {
            return Task.FromResult(Result.Ok(execution));
        }

        return Task.FromResult(Result.Fail<CWorkflowExecution>(
            OdinErrors.WorkflowNotFound(workflowId)));
    }

    public Task<Result<CWorkflowExecution>> UpdateAsync(
        CWorkflowExecution execution,
        int expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentOutOfRangeException.ThrowIfLessThan(expectedVersion, 0);

        var key = (execution.NamespaceId, execution.WorkflowId, execution.RunId);

        lock (_sync)
        {
            if (!_executions.TryGetValue(key, out var existing))
            {
                return Task.FromResult(Result.Fail<CWorkflowExecution>(
                    OdinErrors.WorkflowNotFound(execution.WorkflowId, execution.RunId.ToString())));
            }

            if (existing.Version != expectedVersion)
            {
                return Task.FromResult(Result.Fail<CWorkflowExecution>(
                    OdinErrors.ConcurrencyConflict("workflowExecution", expectedVersion, existing.Version)));
            }

            var now = DateTimeOffset.UtcNow;
            var updated = NormalizeExecution(execution, now, expectedVersion + 1, existing);

            _executions[key] = updated;
            _currentRuns[(execution.NamespaceId, execution.WorkflowId)] = execution.RunId;

            return Task.FromResult(Result.Ok(updated));
        }
    }

    public Task<Result<CWorkflowExecution>> UpdateWithEventIdAsync(
        CWorkflowExecution execution,
        int expectedVersion,
        long newEventId,
        CancellationToken cancellationToken = default)
    {
        var updateResult = UpdateAsync(
            execution with { NextEventId = newEventId },
            expectedVersion,
            cancellationToken);

        return updateResult;
    }

    public Task<Result<IReadOnlyList<WorkflowExecutionInfo>>> ListAsync(
        string namespaceId,
        WorkflowState? state = null,
        int pageSize = 100,
        string? pageToken = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceId);

        if (!Guid.TryParse(namespaceId, out var nsId))
        {
            return Task.FromResult(Result.Fail<IReadOnlyList<WorkflowExecutionInfo>>(
                OdinErrors.WorkflowNotFound(namespaceId)));
        }

        var normalizedPageSize = pageSize <= 0 ? 100 : pageSize;
        var offset = 0;

        if (!string.IsNullOrWhiteSpace(pageToken) &&
            int.TryParse(pageToken, out var parsedOffset) &&
            parsedOffset >= 0)
        {
            offset = parsedOffset;
        }

        var filtered = _executions
            .Where(pair => pair.Key.NamespaceId == nsId)
            .Select(pair => pair.Value)
            .Where(exec => state is null || exec.WorkflowState == state)
            .OrderByDescending(exec => exec.StartedAt)
            .ToList();

        var page = filtered
            .Skip(offset)
            .Take(normalizedPageSize)
            .Select(ToExecutionInfo)
            .ToList();

        return Task.FromResult(Result.Ok<IReadOnlyList<WorkflowExecutionInfo>>(page));
    }

    public int CalculateShardId(string workflowId, int shardCount = 512)
        => HashingUtilities.CalculateShardId(workflowId, shardCount);

    public Task<Result<Unit>> TerminateAsync(
        string namespaceId,
        string workflowId,
        string runId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        if (!Guid.TryParse(namespaceId, out var nsId) ||
            !Guid.TryParse(runId, out var runGuid))
        {
            return Task.FromResult(Result.Fail<Unit>(
                OdinErrors.WorkflowNotFound(workflowId, runId)));
        }

        var key = (nsId, workflowId, runGuid);

        lock (_sync)
        {
            if (!_executions.TryGetValue(key, out var existing))
            {
                return Task.FromResult(Result.Fail<Unit>(
                    OdinErrors.WorkflowNotFound(workflowId, runId)));
            }

            var now = DateTimeOffset.UtcNow;
            var terminated = existing with
            {
                WorkflowState = WorkflowState.Terminated,
                CompletionEventId = existing.CompletionEventId ?? existing.NextEventId,
                LastUpdatedAt = now,
                CompletedAt = now
            };

            _executions[key] = terminated;
        }

        return Task.FromResult(Result.Ok(Unit.Value));
    }

    private static CWorkflowExecution NormalizeExecution(
        CWorkflowExecution execution,
        DateTimeOffset updateTimestamp,
        long newVersion,
        CWorkflowExecution existing)
    {
        var completedAt = execution.CompletedAt ??
                          (IsTerminalState(execution.WorkflowState) ? updateTimestamp : existing.CompletedAt);

        var startedAt = execution.StartedAt == default ? existing.StartedAt : execution.StartedAt;
        var shardId = execution.ShardId != 0 ? execution.ShardId : existing.ShardId;

        return execution with
        {
            Version = newVersion,
            LastUpdatedAt = updateTimestamp,
            CompletedAt = completedAt,
            StartedAt = startedAt,
            ShardId = shardId
        };
    }

    private static WorkflowExecutionInfo ToExecutionInfo(CWorkflowExecution execution)
    {
        var closeTime = execution.CompletedAt;
        var duration = closeTime.HasValue
            ? closeTime.Value - execution.StartedAt
            : (TimeSpan?)null;

        ParentExecutionInfo? parent = null;
        if (!string.IsNullOrWhiteSpace(execution.ParentWorkflowId) &&
            execution.ParentRunId is not null)
        {
            parent = new ParentExecutionInfo
            {
                Namespace = string.Empty,
                WorkflowId = execution.ParentWorkflowId,
                RunId = execution.ParentRunId.Value.ToString()
            };
        }

        return new WorkflowExecutionInfo
        {
            WorkflowId = execution.WorkflowId,
            RunId = execution.RunId.ToString(),
            WorkflowType = execution.WorkflowType,
            TaskQueue = execution.TaskQueue,
            Status = MapWorkflowState(execution.WorkflowState),
            StartTime = execution.StartedAt,
            CloseTime = closeTime,
            ExecutionDuration = duration,
            HistoryLength = Math.Max(0, execution.NextEventId - 1),
            ParentExecution = parent,
            SearchAttributes = null,
            Memo = null
        };
    }

    private static CWorkflowStatus MapWorkflowState(WorkflowState state) => state switch
    {
        WorkflowState.Running => CWorkflowStatus.Running,
        WorkflowState.Completed => CWorkflowStatus.Completed,
        WorkflowState.Failed => CWorkflowStatus.Failed,
        WorkflowState.Canceled => CWorkflowStatus.Canceled,
        WorkflowState.Terminated => CWorkflowStatus.Terminated,
        WorkflowState.ContinuedAsNew => CWorkflowStatus.ContinuedAsNew,
        WorkflowState.TimedOut => CWorkflowStatus.TimedOut,
        _ => CWorkflowStatus.Unspecified
    };

    private static bool IsTerminalState(WorkflowState state) => state is WorkflowState.Completed
        or WorkflowState.Failed
        or WorkflowState.Canceled
        or WorkflowState.Terminated
        or WorkflowState.TimedOut;
}
