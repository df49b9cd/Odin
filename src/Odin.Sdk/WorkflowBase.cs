using Hugo;

namespace Odin.Sdk;

/// <summary>
/// Base class for Odin workflows that provides strongly typed access to the ambient workflow runtime.
/// </summary>
/// <typeparam name="TInput">Workflow input type.</typeparam>
/// <typeparam name="TOutput">Workflow output type.</typeparam>
public abstract class WorkflowBase<TInput, TOutput> : IWorkflow<TInput, TOutput>
{
    public Task<Result<TOutput>> ExecuteAsync(TInput input, CancellationToken cancellationToken)
    {
        var context = WorkflowRuntime.Context;
        return ExecuteAsync(context, input, cancellationToken);
    }

    /// <summary>
    /// Executes the workflow using the provided context.
    /// </summary>
    protected abstract Task<Result<TOutput>> ExecuteAsync(
        WorkflowExecutionContext context,
        TInput input,
        CancellationToken cancellationToken);

    /// <summary>
    /// Captures a deterministic side-effect scoped to this workflow.
    /// </summary>
    protected Task<Result<T>> CaptureAsync<T>(
        string effectId,
        Func<CancellationToken, Task<Result<T>>> effect,
        CancellationToken cancellationToken)
        => WorkflowRuntime.CaptureAsync(effectId, effect, cancellationToken);

    /// <summary>
    /// Captures a deterministic side-effect scoped to this workflow.
    /// </summary>
    protected Task<Result<T>> CaptureAsync<T>(
        string effectId,
        Func<Task<Result<T>>> effect,
        CancellationToken cancellationToken)
        => WorkflowRuntime.CaptureAsync(effectId, effect, cancellationToken);

    /// <summary>
    /// Captures a deterministic side-effect scoped to this workflow.
    /// </summary>
    protected Task<Result<T>> CaptureAsync<T>(
        string effectId,
        Func<Result<T>> effect)
        => WorkflowRuntime.CaptureAsync(effectId, effect);

    /// <summary>
    /// Requires a workflow version using the ambient version gate.
    /// </summary>
    protected Result<VersionDecision> RequireVersion(
        string changeId,
        int minSupportedVersion,
        int maxSupportedVersion,
        Func<VersionGateContext, int>? initialVersionProvider = null)
        => WorkflowRuntime.RequireVersion(changeId, minSupportedVersion, maxSupportedVersion, initialVersionProvider);

    protected WorkflowExecutionContext Context => WorkflowRuntime.Context;

    protected TimeProvider TimeProvider => WorkflowRuntime.TimeProvider;
}
