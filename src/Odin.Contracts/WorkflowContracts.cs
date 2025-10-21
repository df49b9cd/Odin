namespace Odin.Contracts;

/// <summary>
/// Represents a workflow execution request
/// </summary>
public record StartWorkflowRequest
{
    /// <summary>
    /// The namespace for this workflow
    /// </summary>
    public required string Namespace { get; init; }

    /// <summary>
    /// The workflow type name
    /// </summary>
    public required string WorkflowType { get; init; }

    /// <summary>
    /// Unique identifier for this workflow execution
    /// </summary>
    public required string WorkflowId { get; init; }

    /// <summary>
    /// The task queue to route this workflow to
    /// </summary>
    public required string TaskQueue { get; init; }

    /// <summary>
    /// Serialized workflow input
    /// </summary>
    public string? Input { get; init; }

    /// <summary>
    /// Workflow execution timeout
    /// </summary>
    public TimeSpan? ExecutionTimeout { get; init; }

    /// <summary>
    /// Workflow run timeout
    /// </summary>
    public TimeSpan? RunTimeout { get; init; }
}

/// <summary>
/// Represents a workflow execution response
/// </summary>
public record StartWorkflowResponse
{
    /// <summary>
    /// The workflow execution identifier
    /// </summary>
    public required string WorkflowId { get; init; }

    /// <summary>
    /// The run identifier for this execution
    /// </summary>
    public required string RunId { get; init; }
}
