using Hugo;
using Odin.Contracts;
using static Hugo.Go;

namespace Odin.Persistence.Interfaces;

/// <summary>
/// Repository for workflow visibility and advanced search operations.
/// Supports filtering, sorting, and full-text search on workflow metadata.
/// </summary>
public interface IVisibilityRepository
{
    /// <summary>
    /// Upserts visibility record for a workflow execution.
    /// Called when workflow state changes to update searchable attributes.
    /// </summary>
    Task<Result<Unit>> UpsertAsync(
        string namespaceId,
        string workflowId,
        string runId,
        WorkflowExecutionInfo info,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists workflow executions with filtering and pagination.
    /// </summary>
    Task<Result<ListWorkflowExecutionsResponse>> ListAsync(
        ListWorkflowExecutionsRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches workflow executions using a query string.
    /// Supports SQL-like syntax: "WorkflowType = 'OrderProcessing' AND Status = 'Running'"
    /// </summary>
    Task<Result<ListWorkflowExecutionsResponse>> SearchAsync(
        string namespaceId,
        string query,
        int pageSize = 100,
        string? pageToken = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts workflow executions matching a query.
    /// </summary>
    Task<Result<long>> CountAsync(
        string namespaceId,
        string? query = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds or updates tags for a workflow execution.
    /// </summary>
    Task<Result<Unit>> UpdateTagsAsync(
        string namespaceId,
        string workflowId,
        string runId,
        IReadOnlyList<string> tags,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches workflows by tags.
    /// </summary>
    Task<Result<ListWorkflowExecutionsResponse>> SearchByTagsAsync(
        string namespaceId,
        IReadOnlyList<string> tags,
        bool matchAll = false,
        int pageSize = 100,
        string? pageToken = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Archives visibility records older than retention period.
    /// </summary>
    Task<Result<int>> ArchiveOldRecordsAsync(
        string namespaceId,
        DateTimeOffset olderThan,
        int batchSize = 1000,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes visibility record for a workflow execution.
    /// </summary>
    Task<Result<Unit>> DeleteAsync(
        string namespaceId,
        string workflowId,
        string runId,
        CancellationToken cancellationToken = default);
}
