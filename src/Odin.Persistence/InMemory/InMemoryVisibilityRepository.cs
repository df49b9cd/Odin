using System.Collections.Concurrent;
using Hugo;
using Odin.Contracts;
using Odin.Persistence.Interfaces;
using static Hugo.Go;

namespace Odin.Persistence.InMemory;

/// <summary>
/// In-memory visibility repository that supports simple list and search operations.
/// </summary>
public sealed class InMemoryVisibilityRepository : IVisibilityRepository
{
    private readonly ConcurrentDictionary<(string NamespaceId, string WorkflowId, string RunId), WorkflowExecutionInfo> _store = new();

    public Task<Result<Unit>> UpsertAsync(
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

        _store[(namespaceId, workflowId, runId)] = info;

        return Task.FromResult(Result.Ok(Unit.Value));
    }

    public Task<Result<ListWorkflowExecutionsResponse>> ListAsync(
        ListWorkflowExecutionsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Namespace);

        var pageSize = request.PageSize is > 0 ? request.PageSize.Value : 100;
        var offset = 0;

        if (!string.IsNullOrWhiteSpace(request.NextPageToken) &&
            int.TryParse(request.NextPageToken, out var parsedOffset) &&
            parsedOffset >= 0)
        {
            offset = parsedOffset;
        }

        var namespaceMatches = _store
            .Where(entry => string.Equals(entry.Key.NamespaceId, request.Namespace, StringComparison.Ordinal))
            .Select(entry => entry.Value)
            .OrderByDescending(info => info.StartTime)
            .ToList();

        var page = namespaceMatches
            .Skip(offset)
            .Take(pageSize)
            .ToList();

        var nextToken = offset + page.Count < namespaceMatches.Count
            ? (offset + page.Count).ToString()
            : null;

        var response = new ListWorkflowExecutionsResponse
        {
            Executions = page,
            NextPageToken = nextToken
        };

        return Task.FromResult(Result.Ok(response));
    }

    public Task<Result<ListWorkflowExecutionsResponse>> SearchAsync(
        string namespaceId,
        string query,
        int pageSize = 100,
        string? pageToken = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceId);

        var offset = 0;
        if (!string.IsNullOrWhiteSpace(pageToken) &&
            int.TryParse(pageToken, out var parsedOffset) &&
            parsedOffset >= 0)
        {
            offset = parsedOffset;
        }

        var normalizedQuery = query?.Trim();

        var filtered = _store
            .Where(entry => string.Equals(entry.Key.NamespaceId, namespaceId, StringComparison.Ordinal))
            .Select(entry => entry.Value)
            .Where(info =>
                string.IsNullOrEmpty(normalizedQuery) ||
                info.WorkflowId.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase) ||
                info.WorkflowType.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(info => info.StartTime)
            .ToList();

        var page = filtered.Skip(offset).Take(pageSize).ToList();
        var nextToken = offset + page.Count < filtered.Count
            ? (offset + page.Count).ToString()
            : null;

        var response = new ListWorkflowExecutionsResponse
        {
            Executions = page,
            NextPageToken = nextToken
        };

        return Task.FromResult(Result.Ok(response));
    }

    public Task<Result<long>> CountAsync(
        string namespaceId,
        string? query = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceId);

        var normalizedQuery = query?.Trim();

        var count = _store
            .Where(entry => string.Equals(entry.Key.NamespaceId, namespaceId, StringComparison.Ordinal))
            .Select(entry => entry.Value)
            .Count(info =>
                string.IsNullOrEmpty(normalizedQuery) ||
                info.WorkflowId.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase) ||
                info.WorkflowType.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(Result.Ok((long)count));
    }

    public Task<Result<Unit>> UpdateTagsAsync(
        string namespaceId,
        string workflowId,
        string runId,
        IReadOnlyList<string> tags,
        CancellationToken cancellationToken = default)
    {
        // Tag support is not implemented for in-memory visibility; treat as a no-op.
        return Task.FromResult(Result.Ok(Unit.Value));
    }

    public Task<Result<ListWorkflowExecutionsResponse>> SearchByTagsAsync(
        string namespaceId,
        IReadOnlyList<string> tags,
        bool matchAll = false,
        int pageSize = 100,
        string? pageToken = null,
        CancellationToken cancellationToken = default)
    {
        // Tag search is not supported in-memory; return empty results.
        var response = new ListWorkflowExecutionsResponse
        {
            Executions = Array.Empty<WorkflowExecutionInfo>(),
            NextPageToken = null
        };

        return Task.FromResult(Result.Ok(response));
    }

    public Task<Result<int>> ArchiveOldRecordsAsync(
        string namespaceId,
        DateTimeOffset olderThan,
        int batchSize = 1000,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceId);

        var removed = 0;

        foreach (var key in _store.Keys)
        {
            if (!string.Equals(key.NamespaceId, namespaceId, StringComparison.Ordinal))
            {
                continue;
            }

            if (_store.TryGetValue(key, out var info) &&
                info.CloseTime is { } closeTime &&
                closeTime < olderThan)
            {
                if (_store.TryRemove(key, out _))
                {
                    removed++;
                }
            }
        }

        return Task.FromResult(Result.Ok(removed));
    }

    public Task<Result<Unit>> DeleteAsync(
        string namespaceId,
        string workflowId,
        string runId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        _store.TryRemove((namespaceId, workflowId, runId), out _);

        return Task.FromResult(Result.Ok(Unit.Value));
    }
}
