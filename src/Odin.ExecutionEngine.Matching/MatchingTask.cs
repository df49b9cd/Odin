using Hugo;
using Odin.Contracts;
using Odin.Sdk;
using static Hugo.Go;
using Unit = Hugo.Go.Unit;

namespace Odin.ExecutionEngine.Matching;

public sealed class MatchingTask
{
    private readonly Func<CancellationToken, Task<Result<Unit>>> _complete;
    private readonly Func<string, bool, CancellationToken, Task<Result<Unit>>> _fail;
    private readonly Func<CancellationToken, Task<Result<TaskLease>>> _heartbeat;

    internal MatchingTask(
        TaskDispatchItem dispatchItem,
        Func<CancellationToken, Task<Result<Unit>>> complete,
        Func<string, bool, CancellationToken, Task<Result<Unit>>> fail,
        Func<CancellationToken, Task<Result<TaskLease>>> heartbeat)
    {
        WorkflowTask = dispatchItem.WorkflowTask;
        Lease = dispatchItem.Lease;
        _complete = complete;
        _fail = fail;
        _heartbeat = heartbeat;
    }

    public WorkflowTask WorkflowTask { get; }

    public TaskLease Lease { get; }

    public Task<Result<Unit>> CompleteAsync(CancellationToken cancellationToken = default)
        => _complete(cancellationToken);

    public Task<Result<Unit>> FailAsync(string reason, bool requeue, CancellationToken cancellationToken = default)
        => _fail(reason, requeue, cancellationToken);

    public Task<Result<TaskLease>> HeartbeatAsync(CancellationToken cancellationToken = default)
        => _heartbeat(cancellationToken);
}
