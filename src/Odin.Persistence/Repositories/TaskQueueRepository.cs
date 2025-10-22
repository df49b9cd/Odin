using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Hugo;
using Microsoft.Extensions.Logging;
using Npgsql;
using Odin.Contracts;
using Odin.Core;
using Odin.Persistence.Interfaces;
using static Hugo.Go;

namespace Odin.Persistence.Repositories;

/// <summary>
/// PostgreSQL implementation of task queue repository using Dapper.
/// </summary>
public sealed class TaskQueueRepository(
    IDbConnectionFactory connectionFactory,
    ILogger<TaskQueueRepository> logger,
    TimeProvider? timeProvider = null) : ITaskQueueRepository
{
    private const int DefaultLeaseDurationSeconds = 60;
    private const int DefaultHeartbeatExtensionSeconds = 60;
    private const int DefaultRequeueDelaySeconds = 5;

    private readonly IDbConnectionFactory _connectionFactory = connectionFactory;
    private readonly ILogger<TaskQueueRepository> _logger = logger;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<Result<Guid>> EnqueueAsync(
        TaskQueueItem task,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentException.ThrowIfNullOrWhiteSpace(task.TaskQueueName);
        ArgumentException.ThrowIfNullOrWhiteSpace(task.WorkflowId);

        var partitionHash = task.PartitionHash != 0
            ? task.PartitionHash
            : HashingUtilities.CalculatePartitionHash(task.TaskQueueName);

        var connectionResult = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        if (connectionResult.IsFailure)
        {
            return Result.Fail<Guid>(connectionResult.Error!);
        }

        using var connection = connectionResult.Value;

        const string sql = @"
INSERT INTO task_queues (
    namespace_id,
    task_queue_name,
    task_queue_type,
    task_id,
    workflow_id,
    run_id,
    scheduled_at,
    expiry_at,
    task_data,
    partition_hash,
    created_at
) VALUES (
    @NamespaceId,
    @TaskQueueName,
    @TaskQueueType,
    @TaskId,
    @WorkflowId,
    @RunId,
    @ScheduledAt,
    @ExpiryAt,
    CAST(@TaskData AS JSONB),
    @PartitionHash,
    @CreatedAt
)";

        var now = _timeProvider.GetUtcNow();
        var parameters = new
        {
            NamespaceId = task.NamespaceId,
            TaskQueueName = task.TaskQueueName,
            TaskQueueType = ToDatabaseType(task.TaskQueueType),
            TaskId = task.TaskId,
            WorkflowId = task.WorkflowId,
            RunId = task.RunId,
            ScheduledAt = task.ScheduledAt.UtcDateTime,
            ExpiryAt = task.ExpiryAt?.UtcDateTime,
            TaskData = task.TaskData.RootElement.GetRawText(),
            PartitionHash = partitionHash,
            CreatedAt = now.UtcDateTime
        };

        try
        {
            await connection.ExecuteAsync(sql, parameters);
            return Result.Ok(Guid.NewGuid());
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            _logger.LogWarning(
                ex,
                "Duplicate task detected for queue {QueueName} (taskId={TaskId}, namespaceId={NamespaceId})",
                task.TaskQueueName,
                task.TaskId,
                task.NamespaceId);

            return Result.Fail<Guid>(
                Error.From("Task already exists in queue.", OdinErrorCodes.TaskQueueError));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to enqueue task {TaskId} for queue {QueueName}",
                task.TaskId,
                task.TaskQueueName);

            return Result.Fail<Guid>(
                Error.From($"Failed to enqueue task: {ex.Message}", OdinErrorCodes.TaskQueueError));
        }
    }

    public async Task<Result<TaskLease?>> PollAsync(
        string queueName,
        string workerIdentity,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerIdentity);

        var connectionResult = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        if (connectionResult.IsFailure)
        {
            return Result.Fail<TaskLease?>(connectionResult.Error!);
        }

        using var connection = connectionResult.Value;
        using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);

        try
        {
            const string metadataSql = @"
SELECT namespace_id, task_queue_type
FROM task_queues
WHERE task_queue_name = @QueueName
  AND (expiry_at IS NULL OR expiry_at > NOW())
ORDER BY scheduled_at
LIMIT 1
FOR UPDATE SKIP LOCKED";

            var metadata = await connection.QuerySingleOrDefaultAsync<QueueMetadataRow>(
                metadataSql,
                new { QueueName = queueName },
                transaction);

            if (metadata is null)
            {
                transaction.Commit();
                return Result.Ok<TaskLease?>(null);
            }

            const string functionSql = @"
SELECT task_id, lease_id
FROM get_next_task(@NamespaceId, @QueueName, @TaskQueueType, @WorkerIdentity, @LeaseDurationSeconds)";

            var functionResult = await connection.QuerySingleOrDefaultAsync<GetNextTaskResult>(
                functionSql,
                new
                {
                    metadata.NamespaceId,
                    QueueName = queueName,
                    metadata.TaskQueueType,
                    WorkerIdentity = workerIdentity,
                    LeaseDurationSeconds = DefaultLeaseDurationSeconds
                },
                transaction);

            if (functionResult is null)
            {
                transaction.Commit();
                return Result.Ok<TaskLease?>(null);
            }

            const string taskSql = @"
SELECT
    namespace_id,
    task_queue_name,
    task_queue_type,
    task_id,
    workflow_id,
    run_id,
    scheduled_at,
    expiry_at,
    task_data::text AS task_data,
    partition_hash
FROM task_queues
WHERE namespace_id = @NamespaceId
  AND task_queue_name = @QueueName
  AND task_queue_type = @TaskQueueType
  AND task_id = @TaskId";

            var taskRow = await connection.QuerySingleAsync<TaskQueueRow>(
                taskSql,
                new
                {
                    metadata.NamespaceId,
                    QueueName = queueName,
                    metadata.TaskQueueType,
                    TaskId = functionResult.TaskId
                },
                transaction);

            const string leaseSql = @"
SELECT lease_id, leased_at, lease_expires_at, heartbeat_at, attempt_count, worker_identity
FROM task_queue_leases
WHERE lease_id = @LeaseId";

            var leaseRow = await connection.QuerySingleAsync<TaskQueueLeaseRow>(
                leaseSql,
                new { LeaseId = functionResult.LeaseId },
                transaction);

            transaction.Commit();

            return Result.Ok<TaskLease?>(CreateTaskLease(taskRow, leaseRow));
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            _logger.LogError(
                ex,
                "Failed to poll task queue {QueueName} for worker {WorkerIdentity}",
                queueName,
                workerIdentity);

            return Result.Fail<TaskLease?>(
                Error.From($"Failed to poll task queue: {ex.Message}", OdinErrorCodes.TaskQueueError));
        }
    }

    public async Task<Result<TaskLease>> HeartbeatAsync(
        Guid leaseId,
        CancellationToken cancellationToken = default)
    {
        if (leaseId == Guid.Empty)
        {
            return Result.Fail<TaskLease>(
                Error.From("Lease identifier is required.", OdinErrorCodes.TaskLeaseExpired));
        }

        var connectionResult = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        if (connectionResult.IsFailure)
        {
            return Result.Fail<TaskLease>(connectionResult.Error!);
        }

        using var connection = connectionResult.Value;

        const string sql = @"
UPDATE task_queue_leases tql
SET heartbeat_at = NOW(),
    lease_expires_at = NOW() + (@LeaseExtensionSeconds || ' seconds')::INTERVAL
FROM task_queues tq
WHERE tql.lease_id = @LeaseId
  AND tq.namespace_id = tql.namespace_id
  AND tq.task_queue_name = tql.task_queue_name
  AND tq.task_queue_type = tql.task_queue_type
  AND tq.task_id = tql.task_id
RETURNING
    tq.namespace_id,
    tq.task_queue_name,
    tq.task_queue_type,
    tq.task_id,
    tq.workflow_id,
    tq.run_id,
    tq.scheduled_at,
    tq.expiry_at,
    tq.task_data::text AS task_data,
    tq.partition_hash,
    tql.lease_id,
    tql.worker_identity,
    tql.leased_at,
    tql.lease_expires_at,
    tql.heartbeat_at,
    tql.attempt_count";

        try
        {
            var row = await connection.QuerySingleOrDefaultAsync<TaskQueueJoinedLeaseRow>(
                sql,
                new
                {
                    LeaseId = leaseId,
                    LeaseExtensionSeconds = DefaultHeartbeatExtensionSeconds
                });

            if (row is null)
            {
                return Result.Fail<TaskLease>(
                    Error.From($"Lease {leaseId} not found.", OdinErrorCodes.TaskLeaseExpired));
            }

            return Result.Ok(row.ToTaskLease());
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to heartbeat lease {LeaseId}",
                leaseId);

            return Result.Fail<TaskLease>(
                Error.From($"Failed to heartbeat lease: {ex.Message}", OdinErrorCodes.TaskQueueError));
        }
    }

    public async Task<Result<Unit>> CompleteAsync(
        Guid leaseId,
        CancellationToken cancellationToken = default)
    {
        if (leaseId == Guid.Empty)
        {
            return Result.Fail<Unit>(
                Error.From("Lease identifier is required.", OdinErrorCodes.TaskLeaseExpired));
        }

        var connectionResult = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        if (connectionResult.IsFailure)
        {
            return Result.Fail<Unit>(connectionResult.Error!);
        }

        using var connection = connectionResult.Value;
        using var transaction = connection.BeginTransaction();

        try
        {
            const string deleteLeaseSql = @"
DELETE FROM task_queue_leases
WHERE lease_id = @LeaseId
RETURNING namespace_id, task_queue_name, task_queue_type, task_id";

            var leaseKey = await connection.QuerySingleOrDefaultAsync<LeaseKeyRow>(
                deleteLeaseSql,
                new { LeaseId = leaseId },
                transaction);

            if (leaseKey is null)
            {
                transaction.Rollback();
                return Result.Fail<Unit>(
                    Error.From($"Lease {leaseId} not found.", OdinErrorCodes.TaskLeaseExpired));
            }

            const string deleteTaskSql = @"
DELETE FROM task_queues
WHERE namespace_id = @NamespaceId
  AND task_queue_name = @TaskQueueName
  AND task_queue_type = @TaskQueueType
  AND task_id = @TaskId";

            await connection.ExecuteAsync(
                deleteTaskSql,
                leaseKey,
                transaction);

            transaction.Commit();
            return Result.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            _logger.LogError(
                ex,
                "Failed to complete lease {LeaseId}",
                leaseId);

            return Result.Fail<Unit>(
                Error.From($"Failed to complete task: {ex.Message}", OdinErrorCodes.TaskQueueError));
        }
    }

    public async Task<Result<Unit>> FailAsync(
        Guid leaseId,
        string reason,
        bool requeue = true,
        CancellationToken cancellationToken = default)
    {
        if (leaseId == Guid.Empty)
        {
            return Result.Fail<Unit>(
                Error.From("Lease identifier is required.", OdinErrorCodes.TaskLeaseExpired));
        }

        var connectionResult = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        if (connectionResult.IsFailure)
        {
            return Result.Fail<Unit>(connectionResult.Error!);
        }

        using var connection = connectionResult.Value;
        using var transaction = connection.BeginTransaction();

        try
        {
            const string deleteLeaseSql = @"
DELETE FROM task_queue_leases
WHERE lease_id = @LeaseId
RETURNING namespace_id, task_queue_name, task_queue_type, task_id";

            var leaseKey = await connection.QuerySingleOrDefaultAsync<LeaseKeyRow>(
                deleteLeaseSql,
                new { LeaseId = leaseId },
                transaction);

            if (leaseKey is null)
            {
                transaction.Rollback();
                return Result.Fail<Unit>(
                    Error.From($"Lease {leaseId} not found.", OdinErrorCodes.TaskLeaseExpired));
            }

            if (!requeue)
            {
                const string deleteTaskSql = @"
DELETE FROM task_queues
WHERE namespace_id = @NamespaceId
  AND task_queue_name = @TaskQueueName
  AND task_queue_type = @TaskQueueType
  AND task_id = @TaskId";

                await connection.ExecuteAsync(
                    deleteTaskSql,
                    leaseKey,
                    transaction);
            }
            else
            {
                const string rescheduleSql = @"
UPDATE task_queues
SET scheduled_at = NOW() + (@DelaySeconds || ' seconds')::INTERVAL
WHERE namespace_id = @NamespaceId
  AND task_queue_name = @TaskQueueName
  AND task_queue_type = @TaskQueueType
  AND task_id = @TaskId";

                await connection.ExecuteAsync(
                    rescheduleSql,
                    new
                    {
                        leaseKey.NamespaceId,
                        leaseKey.TaskQueueName,
                        leaseKey.TaskQueueType,
                        leaseKey.TaskId,
                        DelaySeconds = DefaultRequeueDelaySeconds
                    },
                    transaction);
            }

            transaction.Commit();

            if (!string.IsNullOrWhiteSpace(reason))
            {
                _logger.LogInformation(
                    "Lease {LeaseId} failed with reason: {Reason}. Requeue={Requeue}",
                    leaseId,
                    reason,
                    requeue);
            }

            return Result.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            _logger.LogError(
                ex,
                "Failed to fail lease {LeaseId}",
                leaseId);

            return Result.Fail<Unit>(
                Error.From($"Failed to fail task: {ex.Message}", OdinErrorCodes.TaskQueueError));
        }
    }

    public async Task<Result<int>> GetQueueDepthAsync(
        string queueName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);

        var connectionResult = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        if (connectionResult.IsFailure)
        {
            return Result.Fail<int>(connectionResult.Error!);
        }

        using var connection = connectionResult.Value;

        const string sql = @"
SELECT COUNT(*)::INT
FROM task_queues tq
WHERE tq.task_queue_name = @QueueName
  AND (tq.expiry_at IS NULL OR tq.expiry_at > NOW())
  AND NOT EXISTS (
      SELECT 1
      FROM task_queue_leases tql
      WHERE tql.namespace_id = tq.namespace_id
        AND tql.task_queue_name = tq.task_queue_name
        AND tql.task_queue_type = tq.task_queue_type
        AND tql.task_id = tq.task_id
        AND tql.lease_expires_at > NOW()
  )";

        try
        {
            var depth = await connection.QuerySingleAsync<int>(
                sql,
                new { QueueName = queueName });

            return Result.Ok(depth);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to get queue depth for {QueueName}",
                queueName);

            return Result.Fail<int>(
                Error.From($"Failed to get queue depth: {ex.Message}", OdinErrorCodes.TaskQueueError));
        }
    }

    public async Task<Result<Dictionary<string, int>>> ListQueuesAsync(
        string? namespaceId = null,
        CancellationToken cancellationToken = default)
    {
        var connectionResult = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        if (connectionResult.IsFailure)
        {
            return Result.Fail<Dictionary<string, int>>(connectionResult.Error!);
        }

        using var connection = connectionResult.Value;

        const string sql = @"
SELECT
    task_queue_name,
    COUNT(*) FILTER (
        WHERE (expiry_at IS NULL OR expiry_at > NOW())
          AND NOT EXISTS (
              SELECT 1
              FROM task_queue_leases tql
              WHERE tql.namespace_id = tq.namespace_id
                AND tql.task_queue_name = tq.task_queue_name
                AND tql.task_queue_type = tq.task_queue_type
                AND tql.task_id = tq.task_id
                AND tql.lease_expires_at > NOW()
          )
    )::INT AS pending_count
FROM task_queues tq
WHERE (@NamespaceId IS NULL OR tq.namespace_id = @NamespaceId::UUID)
GROUP BY task_queue_name";

        try
        {
            var rows = await connection.QueryAsync<(string QueueName, int PendingCount)>(
                sql,
                new { NamespaceId = namespaceId });

            var result = rows.ToDictionary(
                row => row.QueueName,
                row => row.PendingCount,
                StringComparer.OrdinalIgnoreCase);

            return Result.Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list queues");
            return Result.Fail<Dictionary<string, int>>(
                Error.From($"Failed to list queues: {ex.Message}", OdinErrorCodes.TaskQueueError));
        }
    }

    public async Task<Result<int>> ReclaimExpiredLeasesAsync(
        CancellationToken cancellationToken = default)
    {
        var connectionResult = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        if (connectionResult.IsFailure)
        {
            return Result.Fail<int>(connectionResult.Error!);
        }

        using var connection = connectionResult.Value;

        try
        {
            var reclaimed = await connection.ExecuteScalarAsync<int>(
                "SELECT cleanup_expired_leases();");
            return Result.Ok(reclaimed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reclaim expired leases");
            return Result.Fail<int>(
                Error.From($"Failed to reclaim leases: {ex.Message}", OdinErrorCodes.TaskQueueError));
        }
    }

    public async Task<Result<int>> PurgeOldTasksAsync(
        DateTimeOffset olderThan,
        CancellationToken cancellationToken = default)
    {
        var connectionResult = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        if (connectionResult.IsFailure)
        {
            return Result.Fail<int>(connectionResult.Error!);
        }

        using var connection = connectionResult.Value;

        try
        {
            var count = await connection.ExecuteScalarAsync<int>(
                "SELECT cleanup_expired_tasks(@OlderThan);",
                new { OlderThan = olderThan.UtcDateTime });

            return Result.Ok(count);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to purge old tasks older than {OlderThan}",
                olderThan);

            return Result.Fail<int>(
                Error.From($"Failed to purge tasks: {ex.Message}", OdinErrorCodes.TaskQueueError));
        }
    }

    private static TaskLease CreateTaskLease(TaskQueueRow taskRow, TaskQueueLeaseRow leaseRow)
    {
        return new TaskLease
        {
            LeaseId = leaseRow.LeaseId,
            WorkerIdentity = leaseRow.WorkerIdentity,
            LeasedAt = ToUtcDateTimeOffset(leaseRow.LeasedAt),
            LeaseExpiresAt = ToUtcDateTimeOffset(leaseRow.LeaseExpiresAt),
            HeartbeatAt = ToUtcDateTimeOffset(leaseRow.HeartbeatAt),
            AttemptCount = leaseRow.AttemptCount,
            Task = ToTaskQueueItem(taskRow)
        };
    }

    private static TaskQueueItem ToTaskQueueItem(TaskQueueRow row) =>
        new()
        {
            NamespaceId = row.NamespaceId,
            TaskQueueName = row.TaskQueueName,
            TaskQueueType = FromDatabaseType(row.TaskQueueType),
            TaskId = row.TaskId,
            WorkflowId = row.WorkflowId,
            RunId = row.RunId,
            ScheduledAt = ToUtcDateTimeOffset(row.ScheduledAt),
            ExpiryAt = row.ExpiryAt.HasValue ? ToUtcDateTimeOffset(row.ExpiryAt.Value) : (DateTimeOffset?)null,
            TaskData = JsonDocument.Parse(row.TaskData),
            PartitionHash = row.PartitionHash
        };

    private static string ToDatabaseType(TaskQueueType type) =>
        type switch
        {
            TaskQueueType.Workflow => "workflow",
            TaskQueueType.Activity => "activity",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown task queue type")
        };

    private static TaskQueueType FromDatabaseType(string value) =>
        value?.Equals("activity", StringComparison.OrdinalIgnoreCase) == true
            ? TaskQueueType.Activity
            : TaskQueueType.Workflow;

    private static DateTimeOffset ToUtcDateTimeOffset(DateTime value)
    {
        var specified = value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
        return new DateTimeOffset(specified);
    }

    private sealed record QueueMetadataRow(
        Guid NamespaceId,
        string TaskQueueType);

    private sealed record GetNextTaskResult(
        long TaskId,
        Guid LeaseId);

    private sealed record TaskQueueRow(
        Guid NamespaceId,
        string TaskQueueName,
        string TaskQueueType,
        long TaskId,
        string WorkflowId,
        Guid RunId,
        DateTime ScheduledAt,
        DateTime? ExpiryAt,
        string TaskData,
        int PartitionHash);

    private sealed record TaskQueueLeaseRow(
        Guid LeaseId,
        DateTime LeasedAt,
        DateTime LeaseExpiresAt,
        DateTime HeartbeatAt,
        int AttemptCount,
        string WorkerIdentity);

    private sealed record LeaseKeyRow(
        Guid NamespaceId,
        string TaskQueueName,
        string TaskQueueType,
        long TaskId);

    private sealed record TaskQueueJoinedLeaseRow(
        Guid NamespaceId,
        string TaskQueueName,
        string TaskQueueType,
        long TaskId,
        string WorkflowId,
        Guid RunId,
        DateTimeOffset ScheduledAt,
        DateTimeOffset? ExpiryAt,
        string TaskData,
        int PartitionHash,
        Guid LeaseId,
        string WorkerIdentity,
        DateTimeOffset LeasedAt,
        DateTimeOffset LeaseExpiresAt,
        DateTimeOffset HeartbeatAt,
        int AttemptCount)
    {
        public TaskLease ToTaskLease()
        {
            return new TaskLease
            {
                LeaseId = LeaseId,
                WorkerIdentity = WorkerIdentity,
                LeasedAt = LeasedAt,
                LeaseExpiresAt = LeaseExpiresAt,
                HeartbeatAt = HeartbeatAt,
                AttemptCount = AttemptCount,
                Task = new TaskQueueItem
                {
                    NamespaceId = NamespaceId,
                    TaskQueueName = TaskQueueName,
                    TaskQueueType = FromDatabaseType(TaskQueueType),
                    TaskId = TaskId,
                    WorkflowId = WorkflowId,
                    RunId = RunId,
                    ScheduledAt = ScheduledAt,
                    ExpiryAt = ExpiryAt,
                    TaskData = JsonDocument.Parse(TaskData),
                    PartitionHash = PartitionHash
                }
            };
        }
    }
}
