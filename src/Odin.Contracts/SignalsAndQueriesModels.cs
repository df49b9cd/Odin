using System.Text.Json;

namespace Odin.Contracts;

/// <summary>
/// Request to signal a workflow execution
/// </summary>
public sealed record SignalWorkflowRequest
{
    /// <summary>
    /// Namespace
    /// </summary>
    public required string Namespace { get; init; }

    /// <summary>
    /// Workflow identifier
    /// </summary>
    public required string WorkflowId { get; init; }

    /// <summary>
    /// Run identifier (optional, signals current run if not specified)
    /// </summary>
    public string? RunId { get; init; }

    /// <summary>
    /// Signal name
    /// </summary>
    public required string SignalName { get; init; }

    /// <summary>
    /// Signal input payload
    /// </summary>
    public JsonDocument? Input { get; init; }

    /// <summary>
    /// Identity of the caller
    /// </summary>
    public string? Identity { get; init; }
}

/// <summary>
/// Request to query a workflow execution
/// </summary>
public sealed record QueryWorkflowRequest
{
    /// <summary>
    /// Namespace
    /// </summary>
    public required string Namespace { get; init; }

    /// <summary>
    /// Workflow identifier
    /// </summary>
    public required string WorkflowId { get; init; }

    /// <summary>
    /// Run identifier (optional, queries current run if not specified)
    /// </summary>
    public string? RunId { get; init; }

    /// <summary>
    /// Query name
    /// </summary>
    public required string QueryName { get; init; }

    /// <summary>
    /// Query input payload
    /// </summary>
    public JsonDocument? Input { get; init; }

    /// <summary>
    /// Query consistency level
    /// </summary>
    public QueryConsistency Consistency { get; init; } = QueryConsistency.Eventual;
}

/// <summary>
/// Query consistency level
/// </summary>
public enum QueryConsistency
{
    /// <summary>
    /// Query against current workflow state (eventual consistency)
    /// </summary>
    Eventual,

    /// <summary>
    /// Query must reflect all completed workflow tasks (strong consistency)
    /// </summary>
    Strong
}

/// <summary>
/// Response to a workflow query
/// </summary>
public sealed record QueryWorkflowResponse
{
    /// <summary>
    /// Query result
    /// </summary>
    public JsonDocument? Result { get; init; }

    /// <summary>
    /// Query error if failed
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Query execution time
    /// </summary>
    public TimeSpan? QueryDuration { get; init; }
}

/// <summary>
/// Request to terminate a workflow execution
/// </summary>
public sealed record TerminateWorkflowRequest
{
    /// <summary>
    /// Namespace
    /// </summary>
    public required string Namespace { get; init; }

    /// <summary>
    /// Workflow identifier
    /// </summary>
    public required string WorkflowId { get; init; }

    /// <summary>
    /// Run identifier (optional, terminates current run if not specified)
    /// </summary>
    public string? RunId { get; init; }

    /// <summary>
    /// Reason for termination
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>
    /// Termination details
    /// </summary>
    public JsonDocument? Details { get; init; }

    /// <summary>
    /// Identity of the caller
    /// </summary>
    public string? Identity { get; init; }
}

/// <summary>
/// Request to cancel a workflow execution
/// </summary>
public sealed record CancelWorkflowRequest
{
    /// <summary>
    /// Namespace
    /// </summary>
    public required string Namespace { get; init; }

    /// <summary>
    /// Workflow identifier
    /// </summary>
    public required string WorkflowId { get; init; }

    /// <summary>
    /// Run identifier (optional, cancels current run if not specified)
    /// </summary>
    public string? RunId { get; init; }

    /// <summary>
    /// Identity of the caller
    /// </summary>
    public string? Identity { get; init; }
}

/// <summary>
/// Request to describe a workflow execution
/// </summary>
public sealed record DescribeWorkflowExecutionRequest
{
    /// <summary>
    /// Namespace
    /// </summary>
    public required string Namespace { get; init; }

    /// <summary>
    /// Workflow identifier
    /// </summary>
    public required string WorkflowId { get; init; }

    /// <summary>
    /// Run identifier (optional, describes current run if not specified)
    /// </summary>
    public string? RunId { get; init; }
}

/// <summary>
/// Response describing a workflow execution
/// </summary>
public sealed record DescribeWorkflowExecutionResponse
{
    /// <summary>
    /// Workflow execution information
    /// </summary>
    public required WorkflowExecutionInfo ExecutionInfo { get; init; }

    /// <summary>
    /// Pending activities
    /// </summary>
    public IReadOnlyList<PendingActivityInfo>? PendingActivities { get; init; }

    /// <summary>
    /// Pending child workflows
    /// </summary>
    public IReadOnlyList<PendingChildWorkflowInfo>? PendingChildren { get; init; }

    /// <summary>
    /// Pending timers
    /// </summary>
    public IReadOnlyList<PendingTimerInfo>? PendingTimers { get; init; }
}

/// <summary>
/// Information about a pending activity
/// </summary>
public sealed record PendingActivityInfo
{
    /// <summary>
    /// Activity identifier
    /// </summary>
    public required string ActivityId { get; init; }

    /// <summary>
    /// Activity type
    /// </summary>
    public required string ActivityType { get; init; }

    /// <summary>
    /// State
    /// </summary>
    public required string State { get; init; }

    /// <summary>
    /// Scheduled time
    /// </summary>
    public DateTimeOffset? ScheduledTime { get; init; }

    /// <summary>
    /// Last started time
    /// </summary>
    public DateTimeOffset? LastStartedTime { get; init; }

    /// <summary>
    /// Attempt count
    /// </summary>
    public int Attempt { get; init; }

    /// <summary>
    /// Last heartbeat time
    /// </summary>
    public DateTimeOffset? LastHeartbeatTime { get; init; }
}

/// <summary>
/// Information about a pending child workflow
/// </summary>
public sealed record PendingChildWorkflowInfo
{
    /// <summary>
    /// Workflow identifier
    /// </summary>
    public required string WorkflowId { get; init; }

    /// <summary>
    /// Run identifier
    /// </summary>
    public string? RunId { get; init; }

    /// <summary>
    /// Workflow type
    /// </summary>
    public required string WorkflowType { get; init; }

    /// <summary>
    /// Initiated event identifier
    /// </summary>
    public long InitiatedEventId { get; init; }

    /// <summary>
    /// Namespace
    /// </summary>
    public required string Namespace { get; init; }
}

/// <summary>
/// Information about a pending timer
/// </summary>
public sealed record PendingTimerInfo
{
    /// <summary>
    /// Timer identifier
    /// </summary>
    public required string TimerId { get; init; }

    /// <summary>
    /// Scheduled fire time
    /// </summary>
    public required DateTimeOffset FireTime { get; init; }

    /// <summary>
    /// Started event identifier
    /// </summary>
    public long StartedEventId { get; init; }
}

/// <summary>
/// Request to list workflow executions
/// </summary>
public sealed record ListWorkflowExecutionsRequest
{
    /// <summary>
    /// Namespace
    /// </summary>
    public required string Namespace { get; init; }

    /// <summary>
    /// Maximum page size
    /// </summary>
    public int? PageSize { get; init; }

    /// <summary>
    /// Next page token
    /// </summary>
    public string? NextPageToken { get; init; }

    /// <summary>
    /// Query (using visibility search attributes)
    /// </summary>
    public string? Query { get; init; }
}

/// <summary>
/// Response containing list of workflow executions
/// </summary>
public sealed record ListWorkflowExecutionsResponse
{
    /// <summary>
    /// Workflow executions
    /// </summary>
    public required IReadOnlyList<WorkflowExecutionInfo> Executions { get; init; }

    /// <summary>
    /// Next page token
    /// </summary>
    public string? NextPageToken { get; init; }
}
