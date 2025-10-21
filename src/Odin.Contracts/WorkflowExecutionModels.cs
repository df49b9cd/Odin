using System.Text.Json;

namespace Odin.Contracts;

/// <summary>
/// Represents a workflow execution
/// </summary>
public sealed record WorkflowExecution
{
    /// <summary>
    /// Namespace identifier
    /// </summary>
    public required Guid NamespaceId { get; init; }

    /// <summary>
    /// Workflow identifier
    /// </summary>
    public required string WorkflowId { get; init; }

    /// <summary>
    /// Run identifier
    /// </summary>
    public required Guid RunId { get; init; }

    /// <summary>
    /// Workflow type name
    /// </summary>
    public required string WorkflowType { get; init; }

    /// <summary>
    /// Task queue name
    /// </summary>
    public required string TaskQueue { get; init; }

    /// <summary>
    /// Current workflow state
    /// </summary>
    public WorkflowState WorkflowState { get; init; } = WorkflowState.Running;

    /// <summary>
    /// Serialized execution state
    /// </summary>
    public byte[]? ExecutionState { get; init; }

    /// <summary>
    /// Next event ID to be assigned
    /// </summary>
    public long NextEventId { get; init; } = 1;

    /// <summary>
    /// Last processed event ID
    /// </summary>
    public long LastProcessedEventId { get; init; }

    /// <summary>
    /// Workflow timeout in seconds
    /// </summary>
    public int? WorkflowTimeoutSeconds { get; init; }

    /// <summary>
    /// Run timeout in seconds
    /// </summary>
    public int? RunTimeoutSeconds { get; init; }

    /// <summary>
    /// Task timeout in seconds
    /// </summary>
    public int? TaskTimeoutSeconds { get; init; }

    /// <summary>
    /// Retry policy
    /// </summary>
    public JsonDocument? RetryPolicy { get; init; }

    /// <summary>
    /// Cron schedule
    /// </summary>
    public string? CronSchedule { get; init; }

    /// <summary>
    /// Parent workflow ID
    /// </summary>
    public string? ParentWorkflowId { get; init; }

    /// <summary>
    /// Parent run ID
    /// </summary>
    public Guid? ParentRunId { get; init; }

    /// <summary>
    /// Initiated event ID
    /// </summary>
    public long? InitiatedId { get; init; }

    /// <summary>
    /// Completion event ID
    /// </summary>
    public long? CompletionEventId { get; init; }

    /// <summary>
    /// Workflow memo
    /// </summary>
    public JsonDocument? Memo { get; init; }

    /// <summary>
    /// Search attributes
    /// </summary>
    public JsonDocument? SearchAttributes { get; init; }

    /// <summary>
    /// Auto reset points
    /// </summary>
    public JsonDocument? AutoResetPoints { get; init; }

    /// <summary>
    /// Started timestamp
    /// </summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>
    /// Completed timestamp
    /// </summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>
    /// Last updated timestamp
    /// </summary>
    public DateTimeOffset LastUpdatedAt { get; init; }

    /// <summary>
    /// Shard ID
    /// </summary>
    public int ShardId { get; init; }

    /// <summary>
    /// Version for optimistic concurrency control
    /// </summary>
    public long Version { get; init; } = 1;
}

/// <summary>
/// Workflow execution state enumeration
/// </summary>
public enum WorkflowState
{
    Running,
    Completed,
    Failed,
    Canceled,
    Terminated,
    ContinuedAsNew,
    TimedOut
}

/// <summary>
/// Workflow execution status
/// </summary>
public enum WorkflowStatus
{
    Unspecified,
    Running,
    Completed,
    Failed,
    Canceled,
    Terminated,
    ContinuedAsNew,
    TimedOut
}

/// <summary>
/// Describes a workflow execution
/// </summary>
public sealed record WorkflowExecutionInfo
{
    /// <summary>
    /// Workflow identifier
    /// </summary>
    public required string WorkflowId { get; init; }

    /// <summary>
    /// Run identifier
    /// </summary>
    public required string RunId { get; init; }

    /// <summary>
    /// Workflow type
    /// </summary>
    public required string WorkflowType { get; init; }

    /// <summary>
    /// Task queue
    /// </summary>
    public required string TaskQueue { get; init; }

    /// <summary>
    /// Current status
    /// </summary>
    public WorkflowStatus Status { get; init; }

    /// <summary>
    /// Start time
    /// </summary>
    public DateTimeOffset StartTime { get; init; }

    /// <summary>
    /// Close time
    /// </summary>
    public DateTimeOffset? CloseTime { get; init; }

    /// <summary>
    /// Execution time
    /// </summary>
    public TimeSpan? ExecutionDuration { get; init; }

    /// <summary>
    /// History length
    /// </summary>
    public long HistoryLength { get; init; }

    /// <summary>
    /// Parent workflow
    /// </summary>
    public ParentExecutionInfo? ParentExecution { get; init; }

    /// <summary>
    /// Search attributes
    /// </summary>
    public Dictionary<string, object?>? SearchAttributes { get; init; }

    /// <summary>
    /// Memo
    /// </summary>
    public Dictionary<string, object?>? Memo { get; init; }
}

/// <summary>
/// Parent execution information
/// </summary>
public sealed record ParentExecutionInfo
{
    /// <summary>
    /// Namespace
    /// </summary>
    public required string Namespace { get; init; }

    /// <summary>
    /// Workflow ID
    /// </summary>
    public required string WorkflowId { get; init; }

    /// <summary>
    /// Run ID
    /// </summary>
    public required string RunId { get; init; }
}
