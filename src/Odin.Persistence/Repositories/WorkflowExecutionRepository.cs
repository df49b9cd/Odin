using Hugo;
using Odin.Contracts;
using Odin.Core;
using Odin.Persistence.Interfaces;
using static Hugo.Go;
using WorkflowExecution = Odin.Contracts.WorkflowExecution;
using WorkflowStatus = Odin.Contracts.WorkflowStatus;

namespace Odin.Persistence.Repositories;

/// <summary>
/// PostgreSQL/MySQL implementation of workflow execution repository.
/// Phase 1: Stub implementation - to be completed in Phase 2.
/// </summary>
public sealed class WorkflowExecutionRepository : IWorkflowExecutionRepository
{
    public Task<Result<WorkflowExecution>> CreateAsync(WorkflowExecution execution, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Fail<WorkflowExecution>(Error.From("Not implemented", OdinErrorCodes.PersistenceError)));
    }

    public Task<Result<WorkflowExecution>> GetAsync(string namespaceId, string workflowId, string runId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Fail<WorkflowExecution>(Error.From("Not implemented", OdinErrorCodes.WorkflowNotFound)));
    }

    public Task<Result<WorkflowExecution>> GetCurrentAsync(string namespaceId, string workflowId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Fail<WorkflowExecution>(Error.From("Not implemented", OdinErrorCodes.WorkflowNotFound)));
    }

    public Task<Result<WorkflowExecution>> UpdateAsync(WorkflowExecution execution, int expectedVersion, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Fail<WorkflowExecution>(Error.From("Not implemented", OdinErrorCodes.PersistenceError)));
    }

    public Task<Result<WorkflowExecution>> UpdateWithEventIdAsync(WorkflowExecution execution, int expectedVersion, long nextEventId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Fail<WorkflowExecution>(Error.From("Not implemented", OdinErrorCodes.PersistenceError)));
    }

    public Task<Result<IReadOnlyList<WorkflowExecutionInfo>>> ListAsync(string namespaceId, WorkflowState? state = null, int pageSize = 100, string? pageToken = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Ok<IReadOnlyList<WorkflowExecutionInfo>>(Array.Empty<WorkflowExecutionInfo>()));
    }

    public int CalculateShardId(string workflowId, int shardCount = 512)
    {
        return HashingUtilities.CalculateShardId(workflowId, shardCount);
    }

    public Task<Result<Unit>> TerminateAsync(string namespaceId, string workflowId, string runId, string reason, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Ok(Unit.Value));
    }
}
