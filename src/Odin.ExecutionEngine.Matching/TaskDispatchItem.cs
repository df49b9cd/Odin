using System.Text.Json;
using Hugo;
using Microsoft.Extensions.Logging;
using Odin.Contracts;
using Odin.Persistence.Interfaces;
using Odin.Sdk;
using static Hugo.Go;

namespace Odin.ExecutionEngine.Matching;

internal sealed record TaskDispatchItem(
    TaskLease Lease,
    WorkflowTask WorkflowTask,
    Func<CancellationToken, Task<Result<Unit>>> CompleteAsync,
    Func<string, bool, CancellationToken, Task<Result<Unit>>> FailAsync,
    Func<CancellationToken, Task<Result<TaskLease>>> HeartbeatAsync)
{
    public static Result<TaskDispatchItem> Create(
        TaskLease lease,
        ITaskQueueRepository repository,
        JsonSerializerOptions serializerOptions,
        ILogger logger)
    {
        WorkflowTask workflowTask;
        try
        {
            workflowTask = DeserializeWorkflowTask(lease.Task, serializerOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to deserialize workflow task payload for lease {LeaseId}", lease.LeaseId);
            return Result.Fail<TaskDispatchItem>(Error.FromException(ex));
        }

        async Task<Result<Unit>> CompleteAsync(CancellationToken cancellationToken)
            => await repository.CompleteAsync(lease.LeaseId, cancellationToken).ConfigureAwait(false);

        async Task<Result<Unit>> FailAsync(string reason, bool requeue, CancellationToken cancellationToken)
            => await repository.FailAsync(lease.LeaseId, reason, requeue, cancellationToken).ConfigureAwait(false);

        async Task<Result<TaskLease>> HeartbeatAsync(CancellationToken cancellationToken)
            => await repository.HeartbeatAsync(lease.LeaseId, cancellationToken).ConfigureAwait(false);

        return Result.Ok(new TaskDispatchItem(lease, workflowTask, CompleteAsync, FailAsync, HeartbeatAsync));
    }

    private static WorkflowTask DeserializeWorkflowTask(TaskQueueItem item, JsonSerializerOptions serializerOptions)
    {
        var json = item.TaskData.RootElement.GetRawText();
        var workflowTask = JsonSerializer.Deserialize<WorkflowTask>(json, serializerOptions);
        if (workflowTask is null)
        {
            throw new InvalidOperationException("Workflow task payload could not be deserialized.");
        }

        return workflowTask;
    }
}
