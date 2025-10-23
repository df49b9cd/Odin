using Hugo;

namespace Odin.Core;

/// <summary>
/// Common error codes used throughout Odin
/// </summary>
public static class OdinErrorCodes
{
    /// <summary>
    /// Workflow not found
    /// </summary>
    public const string WorkflowNotFound = "WORKFLOW_NOT_FOUND";

    /// <summary>
    /// Workflow already exists
    /// </summary>
    public const string WorkflowAlreadyExists = "WORKFLOW_ALREADY_EXISTS";

    /// <summary>
    /// Workflow execution failed
    /// </summary>
    public const string WorkflowExecutionFailed = "WORKFLOW_EXECUTION_FAILED";

    /// <summary>
    /// Activity not found
    /// </summary>
    public const string ActivityNotFound = "ACTIVITY_NOT_FOUND";

    /// <summary>
    /// Activity execution failed
    /// </summary>
    public const string ActivityExecutionFailed = "ACTIVITY_EXECUTION_FAILED";

    /// <summary>
    /// Namespace not found
    /// </summary>
    public const string NamespaceNotFound = "NAMESPACE_NOT_FOUND";

    /// <summary>
    /// Namespace already exists
    /// </summary>
    public const string NamespaceAlreadyExists = "NAMESPACE_ALREADY_EXISTS";

    /// <summary>
    /// Task queue error
    /// </summary>
    public const string TaskQueueError = "TASK_QUEUE_ERROR";

    /// <summary>
    /// Shard not found or unavailable
    /// </summary>
    public const string ShardUnavailable = "SHARD_UNAVAILABLE";

    /// <summary>
    /// History event error
    /// </summary>
    public const string HistoryEventError = "HISTORY_EVENT_ERROR";

    /// <summary>
    /// Replay determinism violation
    /// </summary>
    public const string ReplayDeterminismViolation = "REPLAY_DETERMINISM_VIOLATION";

    /// <summary>
    /// Persistence error
    /// </summary>
    public const string PersistenceError = "PERSISTENCE_ERROR";

    /// <summary>
    /// Concurrency conflict
    /// </summary>
    public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";

    /// <summary>
    /// Timeout error
    /// </summary>
    public const string Timeout = "TIMEOUT";

    /// <summary>
    /// Rate limit exceeded
    /// </summary>
    public const string RateLimitExceeded = "RATE_LIMIT_EXCEEDED";

    /// <summary>
    /// Task not found
    /// </summary>
    public const string TaskNotFound = "TASK_NOT_FOUND";

    /// <summary>
    /// Task lease expired
    /// </summary>
    public const string TaskLeaseExpired = "TASK_LEASE_EXPIRED";
}

/// <summary>
/// Error factory for creating common Odin errors
/// </summary>
public static class OdinErrors
{
    /// <summary>
    /// Creates a workflow not found error
    /// </summary>
    public static Error WorkflowNotFound(string workflowId, string? runId = null)
    {
        var message = runId is not null
            ? $"Workflow '{workflowId}' with run ID '{runId}' not found"
            : $"Workflow '{workflowId}' not found";

        return Error.From(message, OdinErrorCodes.WorkflowNotFound)
            .WithMetadata(new Dictionary<string, object?>
            {
                ["workflowId"] = workflowId,
                ["runId"] = runId
            });
    }

    /// <summary>
    /// Creates a workflow already exists error
    /// </summary>
    public static Error WorkflowAlreadyExists(string workflowId)
    {
        return Error.From(
            $"Workflow '{workflowId}' already exists",
            OdinErrorCodes.WorkflowAlreadyExists
        ).WithMetadata(new Dictionary<string, object?>
        {
            ["workflowId"] = workflowId
        });
    }

    /// <summary>
    /// Creates a namespace not found error
    /// </summary>
    public static Error NamespaceNotFound(string namespaceName)
    {
        return Error.From(
            $"Namespace '{namespaceName}' not found",
            OdinErrorCodes.NamespaceNotFound
        ).WithMetadata(new Dictionary<string, object?>
        {
            ["namespace"] = namespaceName
        });
    }

    /// <summary>
    /// Creates a namespace already exists error
    /// </summary>
    public static Error NamespaceAlreadyExists(string namespaceName)
    {
        return Error.From(
            $"Namespace '{namespaceName}' already exists",
            OdinErrorCodes.NamespaceAlreadyExists
        ).WithMetadata(new Dictionary<string, object?>
        {
            ["namespace"] = namespaceName
        });
    }

    /// <summary>
    /// Creates a shard unavailable error
    /// </summary>
    public static Error ShardUnavailable(int shardId)
    {
        return Error.From(
            $"Shard {shardId} is unavailable",
            OdinErrorCodes.ShardUnavailable
        ).WithMetadata(new Dictionary<string, object?>
        {
            ["shardId"] = shardId
        });
    }

    /// <summary>
    /// Creates a replay determinism violation error
    /// </summary>
    public static Error ReplayDeterminismViolation(string details)
    {
        return Error.From(
            $"Replay determinism violation: {details}",
            OdinErrorCodes.ReplayDeterminismViolation
        ).WithMetadata(new Dictionary<string, object?>
        {
            ["details"] = details
        });
    }

    /// <summary>
    /// Creates a concurrency conflict error
    /// </summary>
    public static Error ConcurrencyConflict(string resource, long expectedVersion, long actualVersion)
    {
        return Error.From(
            $"Concurrency conflict on '{resource}': expected version {expectedVersion}, actual version {actualVersion}",
            OdinErrorCodes.ConcurrencyConflict
        ).WithMetadata(new Dictionary<string, object?>
        {
            ["resource"] = resource,
            ["expectedVersion"] = expectedVersion,
            ["actualVersion"] = actualVersion
        });
    }

    /// <summary>
    /// Creates a timeout error
    /// </summary>
    public static Error Timeout(string operation, TimeSpan duration)
    {
        return Error.From(
            $"Operation '{operation}' timed out after {duration.TotalSeconds:F2} seconds",
            OdinErrorCodes.Timeout
        ).WithMetadata(new Dictionary<string, object?>
        {
            ["operation"] = operation,
            ["timeoutSeconds"] = duration.TotalSeconds
        });
    }

    /// <summary>
    /// Creates a persistence error
    /// </summary>
    public static Error PersistenceError(string operation, Exception exception)
    {
        return Error.FromException(exception)
            .WithCode(OdinErrorCodes.PersistenceError)
            .WithMetadata(new Dictionary<string, object?>
            {
                ["operation"] = operation
            });
    }
}
