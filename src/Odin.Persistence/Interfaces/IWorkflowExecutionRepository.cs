using Hugo;
using Odin.Contracts;
using static Hugo.Go;
using WorkflowExecution = Odin.Contracts.WorkflowExecution;

namespace Odin.Persistence.Interfaces;

/// <summary>
/// Repository for workflow execution mutable state management.
/// Handles workflow lifecycle state with optimistic locking.
/// </summary>
public interface IWorkflowExecutionRepository
{
    /// <summary>
    /// Creates a new workflow execution record.
    /// </summary>
    Task<Result<WorkflowExecution>> CreateAsync(
        WorkflowExecution execution,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a workflow execution by ID and run ID.
    /// </summary>
    Task<Result<WorkflowExecution>> GetAsync(
        string namespaceId,
        string workflowId,
        string runId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the current (most recent) execution for a workflow ID.
    /// </summary>
    Task<Result<WorkflowExecution>> GetCurrentAsync(
        string namespaceId,
        string workflowId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates workflow execution state with optimistic locking.
    /// Returns error if version mismatch detected.
    /// </summary>
    Task<Result<WorkflowExecution>> UpdateAsync(
        WorkflowExecution execution,
        int expectedVersion,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates workflow execution state and advances next event ID atomically.
    /// </summary>
    Task<Result<WorkflowExecution>> UpdateWithEventIdAsync(
        WorkflowExecution execution,
        int expectedVersion,
        long newEventId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists workflow executions in a namespace with filtering.
    /// </summary>
    Task<Result<IReadOnlyList<WorkflowExecutionInfo>>> ListAsync(
        string namespaceId,
        WorkflowState? state = null,
        int pageSize = 100,
        string? pageToken = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculates the shard ID for a workflow ID.
    /// </summary>
    int CalculateShardId(string workflowId, int shardCount = 512);

    /// <summary>
    /// Terminates a workflow execution (hard delete or mark terminated).
    /// </summary>
    Task<Result<Unit>> TerminateAsync(
        string namespaceId,
        string workflowId,
        string runId,
        string reason,
        CancellationToken cancellationToken = default);
}
