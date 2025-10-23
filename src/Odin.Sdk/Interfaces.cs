using Hugo;

namespace Odin.Sdk;

/// <summary>
/// Base interface for workflow definitions
/// </summary>
public interface IWorkflow
{
}

/// <summary>
/// Workflow interface with input and output types
/// </summary>
/// <typeparam name="TInput">The workflow input type</typeparam>
/// <typeparam name="TOutput">The workflow output type</typeparam>
public interface IWorkflow<TInput, TOutput> : IWorkflow
{
    /// <summary>
    /// Executes the workflow
    /// </summary>
    /// <param name="input">The workflow input</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The workflow result</returns>
    Task<Result<TOutput>> ExecuteAsync(TInput input, CancellationToken cancellationToken);
}

/// <summary>
/// Base interface for activity definitions
/// </summary>
public interface IActivity
{
}

/// <summary>
/// Activity interface with input and output types
/// </summary>
/// <typeparam name="TInput">The activity input type</typeparam>
/// <typeparam name="TOutput">The activity output type</typeparam>
public interface IActivity<TInput, TOutput> : IActivity
{
    /// <summary>
    /// Executes the activity
    /// </summary>
    /// <param name="input">The activity input</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The activity result</returns>
    Task<Result<TOutput>> ExecuteAsync(TInput input, CancellationToken cancellationToken);
}
