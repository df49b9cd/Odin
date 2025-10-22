using Hugo;

namespace Odin.Sdk;

/// <summary>
/// Base class for Odin activities with deterministic runtime support.
/// </summary>
/// <typeparam name="TInput">Activity input type.</typeparam>
/// <typeparam name="TOutput">Activity output type.</typeparam>
public abstract class ActivityBase<TInput, TOutput> : IActivity<TInput, TOutput>
{
    public Task<Result<TOutput>> ExecuteAsync(TInput input, CancellationToken cancellationToken)
        => ExecuteAsync(WorkflowRuntime.Context, input, cancellationToken);

    /// <summary>
    /// Executes the activity using the current workflow execution context.
    /// </summary>
    protected abstract Task<Result<TOutput>> ExecuteAsync(
        WorkflowExecutionContext context,
        TInput input,
        CancellationToken cancellationToken);

    protected Task<Result<T>> CaptureAsync<T>(
        string effectId,
        Func<CancellationToken, Task<Result<T>>> effect,
        CancellationToken cancellationToken)
        => WorkflowRuntime.CaptureAsync(effectId, effect, cancellationToken);

    protected Task<Result<T>> CaptureAsync<T>(
        string effectId,
        Func<Task<Result<T>>> effect,
        CancellationToken cancellationToken)
        => WorkflowRuntime.CaptureAsync(effectId, effect, cancellationToken);

    protected Task<Result<T>> CaptureAsync<T>(
        string effectId,
        Func<Result<T>> effect)
        => WorkflowRuntime.CaptureAsync(effectId, effect);

    protected Result<VersionDecision> RequireVersion(
        string changeId,
        int minSupportedVersion,
        int maxSupportedVersion,
        Func<VersionGateContext, int>? initialVersionProvider = null)
        => WorkflowRuntime.RequireVersion(changeId, minSupportedVersion, maxSupportedVersion, initialVersionProvider);

    protected WorkflowExecutionContext Context => WorkflowRuntime.Context;

    protected TimeProvider TimeProvider => WorkflowRuntime.TimeProvider;
}
