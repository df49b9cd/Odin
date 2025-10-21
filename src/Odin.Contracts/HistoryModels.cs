using System.Text.Json;

namespace Odin.Contracts;

/// <summary>
/// Represents a workflow history event
/// </summary>
public sealed record HistoryEvent
{
    /// <summary>
    /// Event ID (monotonically increasing within a run)
    /// </summary>
    public required long EventId { get; init; }

    /// <summary>
    /// Event type
    /// </summary>
    public required string EventType { get; init; }

    /// <summary>
    /// Event timestamp
    /// </summary>
    public required DateTimeOffset EventTimestamp { get; init; }

    /// <summary>
    /// Task ID (for decision events)
    /// </summary>
    public long TaskId { get; init; } = -1;

    /// <summary>
    /// Event schema version
    /// </summary>
    public long Version { get; init; } = 1;

    /// <summary>
    /// Event payload
    /// </summary>
    public required JsonDocument EventData { get; init; }
}

/// <summary>
/// Common workflow event types
/// </summary>
public static class WorkflowEventType
{
    public const string WorkflowExecutionStarted = "WorkflowExecutionStarted";
    public const string WorkflowExecutionCompleted = "WorkflowExecutionCompleted";
    public const string WorkflowExecutionFailed = "WorkflowExecutionFailed";
    public const string WorkflowExecutionTimedOut = "WorkflowExecutionTimedOut";
    public const string WorkflowExecutionCanceled = "WorkflowExecutionCanceled";
    public const string WorkflowExecutionTerminated = "WorkflowExecutionTerminated";
    public const string WorkflowExecutionContinuedAsNew = "WorkflowExecutionContinuedAsNew";
    
    public const string WorkflowTaskScheduled = "WorkflowTaskScheduled";
    public const string WorkflowTaskStarted = "WorkflowTaskStarted";
    public const string WorkflowTaskCompleted = "WorkflowTaskCompleted";
    public const string WorkflowTaskTimedOut = "WorkflowTaskTimedOut";
    public const string WorkflowTaskFailed = "WorkflowTaskFailed";
    
    public const string ActivityTaskScheduled = "ActivityTaskScheduled";
    public const string ActivityTaskStarted = "ActivityTaskStarted";
    public const string ActivityTaskCompleted = "ActivityTaskCompleted";
    public const string ActivityTaskFailed = "ActivityTaskFailed";
    public const string ActivityTaskTimedOut = "ActivityTaskTimedOut";
    public const string ActivityTaskCanceled = "ActivityTaskCanceled";
    
    public const string TimerStarted = "TimerStarted";
    public const string TimerFired = "TimerFired";
    public const string TimerCanceled = "TimerCanceled";
    
    public const string SignalExternalWorkflowExecutionInitiated = "SignalExternalWorkflowExecutionInitiated";
    public const string SignalExternalWorkflowExecutionFailed = "SignalExternalWorkflowExecutionFailed";
    public const string WorkflowExecutionSignaled = "WorkflowExecutionSignaled";
    
    public const string ChildWorkflowExecutionStarted = "ChildWorkflowExecutionStarted";
    public const string ChildWorkflowExecutionCompleted = "ChildWorkflowExecutionCompleted";
    public const string ChildWorkflowExecutionFailed = "ChildWorkflowExecutionFailed";
    public const string ChildWorkflowExecutionCanceled = "ChildWorkflowExecutionCanceled";
    public const string ChildWorkflowExecutionTimedOut = "ChildWorkflowExecutionTimedOut";
    public const string ChildWorkflowExecutionTerminated = "ChildWorkflowExecutionTerminated";
    
    public const string MarkerRecorded = "MarkerRecorded";
    public const string UpsertWorkflowSearchAttributes = "UpsertWorkflowSearchAttributes";
}

/// <summary>
/// Request to get workflow history
/// </summary>
public sealed record GetWorkflowHistoryRequest
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
    /// Run ID (optional, uses current run if not specified)
    /// </summary>
    public string? RunId { get; init; }

    /// <summary>
    /// Maximum number of events to return
    /// </summary>
    public int? MaximumPageSize { get; init; }

    /// <summary>
    /// Pagination token
    /// </summary>
    public string? NextPageToken { get; init; }

    /// <summary>
    /// Whether to wait for new events
    /// </summary>
    public bool WaitNewEvent { get; init; }
}

/// <summary>
/// Response containing workflow history
/// </summary>
public sealed record GetWorkflowHistoryResponse
{
    /// <summary>
    /// History events
    /// </summary>
    public required IReadOnlyList<HistoryEvent> History { get; init; }

    /// <summary>
    /// Next page token
    /// </summary>
    public string? NextPageToken { get; init; }

    /// <summary>
    /// Whether history is archived
    /// </summary>
    public bool Archived { get; init; }
}

/// <summary>
/// Workflow history batch for replay
/// </summary>
public sealed record WorkflowHistoryBatch
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
    /// First event ID in this batch
    /// </summary>
    public required long FirstEventId { get; init; }

    /// <summary>
    /// Last event ID in this batch
    /// </summary>
    public required long LastEventId { get; init; }

    /// <summary>
    /// Events in this batch
    /// </summary>
    public required IReadOnlyList<HistoryEvent> Events { get; init; }

    /// <summary>
    /// Whether this is the final batch
    /// </summary>
    public bool IsLastBatch { get; init; }
}
