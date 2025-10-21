using System.Text.Json;

namespace Odin.Contracts;

/// <summary>
/// Represents a task in the task queue
/// </summary>
public sealed record TaskQueueItem
{
    /// <summary>
    /// Namespace identifier
    /// </summary>
    public required Guid NamespaceId { get; init; }

    /// <summary>
    /// Task queue name
    /// </summary>
    public required string TaskQueueName { get; init; }

    /// <summary>
    /// Task queue type
    /// </summary>
    public required TaskQueueType TaskQueueType { get; init; }

    /// <summary>
    /// Task identifier
    /// </summary>
    public required long TaskId { get; init; }

    /// <summary>
    /// Workflow identifier
    /// </summary>
    public required string WorkflowId { get; init; }

    /// <summary>
    /// Run identifier
    /// </summary>
    public required Guid RunId { get; init; }

    /// <summary>
    /// Scheduled timestamp
    /// </summary>
    public required DateTimeOffset ScheduledAt { get; init; }

    /// <summary>
    /// Expiry timestamp
    /// </summary>
    public DateTimeOffset? ExpiryAt { get; init; }

    /// <summary>
    /// Task data
    /// </summary>
    public required JsonDocument TaskData { get; init; }

    /// <summary>
    /// Partition hash for distribution
    /// </summary>
    public int PartitionHash { get; init; }
}

/// <summary>
/// Task queue type
/// </summary>
public enum TaskQueueType
{
    Workflow,
    Activity
}

/// <summary>
/// Represents a task lease
/// </summary>
public sealed record TaskLease
{
    /// <summary>
    /// Lease identifier
    /// </summary>
    public required Guid LeaseId { get; init; }

    /// <summary>
    /// Task information
    /// </summary>
    public required TaskQueueItem Task { get; init; }

    /// <summary>
    /// Worker identity
    /// </summary>
    public required string WorkerIdentity { get; init; }

    /// <summary>
    /// Leased timestamp
    /// </summary>
    public required DateTimeOffset LeasedAt { get; init; }

    /// <summary>
    /// Lease expiration
    /// </summary>
    public required DateTimeOffset LeaseExpiresAt { get; init; }

    /// <summary>
    /// Last heartbeat timestamp
    /// </summary>
    public DateTimeOffset HeartbeatAt { get; init; }

    /// <summary>
    /// Attempt count
    /// </summary>
    public int AttemptCount { get; init; } = 1;
}

/// <summary>
/// Request to poll for workflow task
/// </summary>
public sealed record PollWorkflowTaskRequest
{
    /// <summary>
    /// Namespace
    /// </summary>
    public required string Namespace { get; init; }

    /// <summary>
    /// Task queue name
    /// </summary>
    public required string TaskQueue { get; init; }

    /// <summary>
    /// Worker identity
    /// </summary>
    public required string Identity { get; init; }
}

/// <summary>
/// Workflow task response
/// </summary>
public sealed record PollWorkflowTaskResponse
{
    /// <summary>
    /// Task token for completing the task
    /// </summary>
    public required byte[] TaskToken { get; init; }

    /// <summary>
    /// Workflow execution
    /// </summary>
    public required WorkflowExecutionInfo WorkflowExecution { get; init; }

    /// <summary>
    /// Workflow type
    /// </summary>
    public required string WorkflowType { get; init; }

    /// <summary>
    /// Previous started event ID
    /// </summary>
    public long PreviousStartedEventId { get; init; }

    /// <summary>
    /// Started event ID
    /// </summary>
    public long StartedEventId { get; init; }

    /// <summary>
    /// History events since previous task
    /// </summary>
    public IReadOnlyList<HistoryEvent>? History { get; init; }

    /// <summary>
    /// Next page token for history
    /// </summary>
    public string? NextPageToken { get; init; }

    /// <summary>
    /// Attempt number
    /// </summary>
    public int Attempt { get; init; }

    /// <summary>
    /// Scheduled time
    /// </summary>
    public DateTimeOffset? ScheduledTime { get; init; }

    /// <summary>
    /// Started time
    /// </summary>
    public DateTimeOffset? StartedTime { get; init; }

    /// <summary>
    /// Queries to process
    /// </summary>
    public IReadOnlyDictionary<string, JsonDocument>? Queries { get; init; }
}

/// <summary>
/// Request to complete workflow task
/// </summary>
public sealed record CompleteWorkflowTaskRequest
{
    /// <summary>
    /// Task token
    /// </summary>
    public required byte[] TaskToken { get; init; }

    /// <summary>
    /// Decisions made by the workflow
    /// </summary>
    public IReadOnlyList<WorkflowDecision>? Decisions { get; init; }

    /// <summary>
    /// Worker identity
    /// </summary>
    public required string Identity { get; init; }

    /// <summary>
    /// Force create new workflow task
    /// </summary>
    public bool ForceCreateNewWorkflowTask { get; init; }

    /// <summary>
    /// Query results
    /// </summary>
    public IReadOnlyDictionary<string, JsonDocument>? QueryResults { get; init; }
}

/// <summary>
/// Workflow decision
/// </summary>
public sealed record WorkflowDecision
{
    /// <summary>
    /// Decision type
    /// </summary>
    public required string DecisionType { get; init; }

    /// <summary>
    /// Decision attributes
    /// </summary>
    public required JsonDocument Attributes { get; init; }
}

/// <summary>
/// Request to heartbeat task lease
/// </summary>
public sealed record HeartbeatTaskRequest
{
    /// <summary>
    /// Namespace
    /// </summary>
    public required string Namespace { get; init; }

    /// <summary>
    /// Task queue name
    /// </summary>
    public required string TaskQueue { get; init; }

    /// <summary>
    /// Task queue type
    /// </summary>
    public required TaskQueueType TaskQueueType { get; init; }

    /// <summary>
    /// Task identifier
    /// </summary>
    public required long TaskId { get; init; }

    /// <summary>
    /// Lease identifier
    /// </summary>
    public required Guid LeaseId { get; init; }

    /// <summary>
    /// Worker identity
    /// </summary>
    public required string WorkerIdentity { get; init; }
}

/// <summary>
/// Response to heartbeat
/// </summary>
public sealed record HeartbeatTaskResponse
{
    /// <summary>
    /// Whether heartbeat was accepted
    /// </summary>
    public required bool Accepted { get; init; }

    /// <summary>
    /// New lease expiration
    /// </summary>
    public DateTimeOffset? NewLeaseExpiration { get; init; }

    /// <summary>
    /// Whether the task should be canceled
    /// </summary>
    public bool CancelRequested { get; init; }
}
