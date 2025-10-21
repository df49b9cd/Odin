# Odin Persistence Layer - SQL Schemas

This directory contains SQL schema definitions for the Odin orchestration platform persistence layer.

## Directory Structure

```
Schemas/
├── PostgreSQL/          # PostgreSQL 14+ schemas
│   ├── 00_init.sql     # Master initialization script
│   ├── 01_namespaces.sql
│   ├── 02_history_shards.sql
│   ├── 03_workflow_executions.sql
│   ├── 04_history_events.sql
│   ├── 05_task_queues.sql
│   ├── 06_visibility_records.sql
│   ├── 07_timers.sql
│   ├── 08_signals_queries.sql
│   ├── 09_schedules.sql
│   └── 10_functions.sql
└── MySQL/               # MySQL 8.0.19+ schemas (coming soon)
```

## PostgreSQL Schema

### Requirements
- PostgreSQL 14 or higher
- Extensions: None required (uses built-in features)

### Quick Start

1. **Create database:**
```bash
createdb odin_orchestrator
```

2. **Apply schema:**
```bash
cd Schemas/PostgreSQL
psql -d odin_orchestrator -f 00_init.sql
```

### Tables Overview

| Table | Purpose | Key Features |
|-------|---------|--------------|
| `namespaces` | Multi-tenant isolation | RBAC, retention policies |
| `history_shards` | Shard distribution | 512 default shards, lease management |
| `workflow_executions` | Mutable workflow state | Optimistic locking, parent-child relationships |
| `history_events` | Immutable event log | Event sourcing, JSON payloads |
| `task_queues` | Pending tasks | Partition-aware, expiry handling |
| `task_queue_leases` | In-flight tasks | Heartbeat tracking, lease expiry |
| `visibility_records` | Searchable metadata | GIN indexes, advanced queries |
| `workflow_tags` | Tag-based filtering | Many-to-many relationships |
| `workflow_timers` | Durable timers | Fire time indexing |
| `workflow_signals` | Buffered signals | Delivery tracking |
| `workflow_query_results` | Query results cache | Consistency with execution state |
| `workflow_schedules` | Scheduled executions | Cron/interval support |
| `schedule_execution_history` | Schedule run history | Audit trail |

### Indexing Strategy

**Performance-critical indexes:**
- `workflow_executions`: Shard-based queries, active workflow lookups
- `history_events`: Event ID ordering, type-based queries
- `task_queues`: Pending task retrieval with partition awareness
- `visibility_records`: List queries with multiple filter combinations

**Partial indexes:**
- Active workflows only (`workflow_state IN ('running', 'continued_as_new')`)
- Non-expired tasks
- Unprocessed signals

**GIN indexes:**
- `history_events.event_data` for JSON queries
- `visibility_records.search_attributes` for advanced search

### Utility Functions

#### `calculate_shard_id(workflow_id, shard_count)`
Calculate consistent hash-based shard assignment.

**Example:**
```sql
SELECT calculate_shard_id('order-12345', 512);
-- Returns: 342
```

#### `get_next_task(namespace_id, task_queue_name, task_queue_type, worker_identity, lease_duration_seconds)`
Atomically retrieve next available task and create lease.

**Example:**
```sql
SELECT * FROM get_next_task(
    'f47ac10b-58cc-4372-a567-0e02b2c3d479'::UUID,
    'order-processing',
    'workflow',
    'worker-001',
    60
);
```

#### `cleanup_expired_tasks()`
Remove tasks that have exceeded their expiry time.

**Example:**
```sql
SELECT cleanup_expired_tasks();
-- Returns: 42 (number of tasks removed)
```

#### `cleanup_expired_leases()`
Remove expired leases to make tasks available for re-delivery.

**Example:**
```sql
SELECT cleanup_expired_leases();
-- Returns: 15 (number of leases removed)
```

### Automatic Triggers

- `updated_at` columns are automatically updated on row modification
- No manual timestamp management required

## Data Retention

Configure retention per namespace:

```sql
UPDATE namespaces 
SET retention_days = 90 
WHERE namespace_name = 'production';
```

Retention is enforced by cleanup workers that periodically:
1. Archive completed workflow histories (if archival enabled)
2. Delete workflows older than `retention_days`
3. Clean up associated visibility records, timers, signals

## Sharding Strategy

**Default configuration: 512 shards**

Workflow IDs are consistently hashed to determine shard assignment:
- Ensures workflows with the same ID always route to the same shard
- Enables horizontal scaling by distributing shards across history service instances
- Shard ownership is managed via leases in `history_shards` table

**Modifying shard count:**
Shard count should be set during initial deployment and not changed in production.
To use a different shard count:

1. Modify `02_history_shards.sql` initialization
2. Update `calculate_shard_id` function default parameter
3. Configure application `HUGO_ORCHESTRATOR_SHARD_COUNT` environment variable

## Migration Management

For production deployments, consider using migration tools:

### Option 1: Liquibase
```xml
<changeSet id="1" author="odin">
    <sqlFile path="Schemas/PostgreSQL/01_namespaces.sql"/>
</changeSet>
```

### Option 2: Flyway
```
V1__namespaces.sql
V2__history_shards.sql
...
```

### Option 3: DbUp (.NET)
```csharp
var upgrader = DeployChanges.To
    .PostgresqlDatabase(connectionString)
    .WithScriptsFromFileSystem("Schemas/PostgreSQL")
    .LogToConsole()
    .Build();

upgrader.PerformUpgrade();
```

## Performance Tuning

### Connection Pooling
```
# postgresql.conf
max_connections = 200
shared_buffers = 4GB
effective_cache_size = 12GB
work_mem = 50MB
```

### Partitioning (High Volume)

For high-volume deployments, consider partitioning:

**History events by namespace:**
```sql
CREATE TABLE history_events (...)
PARTITION BY HASH (namespace_id);
```

**History events by time range:**
```sql
CREATE TABLE history_events (...)
PARTITION BY RANGE (created_at);
```

### Vacuum Strategy
```sql
-- Enable autovacuum for high-churn tables
ALTER TABLE task_queues SET (autovacuum_vacuum_scale_factor = 0.05);
ALTER TABLE task_queue_leases SET (autovacuum_vacuum_scale_factor = 0.05);
```

## Monitoring Queries

### Check shard distribution:
```sql
SELECT 
    owner_identity,
    COUNT(*) AS shard_count,
    MIN(shard_owner_lease_expiry) AS earliest_expiry,
    MAX(shard_owner_lease_expiry) AS latest_expiry
FROM history_shards
WHERE owner_identity IS NOT NULL
GROUP BY owner_identity;
```

### Active workflow count by state:
```sql
SELECT 
    n.namespace_name,
    we.workflow_state,
    COUNT(*) AS count
FROM workflow_executions we
JOIN namespaces n ON we.namespace_id = n.namespace_id
GROUP BY n.namespace_name, we.workflow_state
ORDER BY n.namespace_name, we.workflow_state;
```

### Task queue backlog:
```sql
SELECT 
    n.namespace_name,
    tq.task_queue_name,
    tq.task_queue_type,
    COUNT(*) AS pending_tasks,
    MIN(tq.scheduled_at) AS oldest_task
FROM task_queues tq
JOIN namespaces n ON tq.namespace_id = n.namespace_id
LEFT JOIN task_queue_leases tql ON 
    tq.namespace_id = tql.namespace_id AND
    tq.task_queue_name = tql.task_queue_name AND
    tq.task_queue_type = tql.task_queue_type AND
    tq.task_id = tql.task_id AND
    tql.lease_expires_at > NOW()
WHERE tql.lease_id IS NULL
  AND (tq.expiry_at IS NULL OR tq.expiry_at > NOW())
GROUP BY n.namespace_name, tq.task_queue_name, tq.task_queue_type
ORDER BY pending_tasks DESC;
```

### Expired leases:
```sql
SELECT 
    COUNT(*) AS expired_lease_count,
    MIN(lease_expires_at) AS oldest_expiry
FROM task_queue_leases
WHERE lease_expires_at < NOW();
```

## Backup Strategy

### Full backup:
```bash
pg_dump -Fc odin_orchestrator > odin_backup_$(date +%Y%m%d).dump
```

### Restore:
```bash
pg_restore -d odin_orchestrator odin_backup_20251021.dump
```

### Point-in-time recovery:
Enable WAL archiving in `postgresql.conf`:
```
wal_level = replica
archive_mode = on
archive_command = 'cp %p /archive/%f'
```

## Security Considerations

1. **Role-based access:**
```sql
CREATE ROLE odin_app WITH LOGIN PASSWORD 'secure_password';
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO odin_app;
```

2. **Read-only visibility access:**
```sql
CREATE ROLE odin_readonly WITH LOGIN PASSWORD 'readonly_password';
GRANT SELECT ON visibility_records, workflow_tags TO odin_readonly;
```

3. **Encryption at rest:**
Enable PostgreSQL transparent data encryption (TDE) or use filesystem-level encryption.

4. **Connection encryption:**
Require SSL/TLS connections in `pg_hba.conf`:
```
hostssl all all 0.0.0.0/0 scram-sha-256
```

## Troubleshooting

### Issue: Slow task queue queries
**Solution:** Check partition distribution and ensure `idx_task_queues_pending` is being used:
```sql
EXPLAIN ANALYZE
SELECT * FROM task_queues
WHERE namespace_id = '...' 
  AND task_queue_name = '...'
  AND (expiry_at IS NULL OR expiry_at > NOW())
LIMIT 1;
```

### Issue: High table bloat
**Solution:** Run manual vacuum:
```sql
VACUUM ANALYZE task_queues;
VACUUM ANALYZE task_queue_leases;
```

### Issue: Deadlocks on workflow updates
**Solution:** Ensure consistent lock ordering and use `FOR UPDATE SKIP LOCKED` where appropriate.

## Related Documentation

- [PostgreSQL Performance Tuning](https://www.postgresql.org/docs/14/performance-tips.html)
- [PostgreSQL Partitioning](https://www.postgresql.org/docs/14/ddl-partitioning.html)
- [Temporal Persistence Documentation](https://docs.temporal.io/temporal-service/persistence)

## Support

For schema-related questions or issues:
- File an issue: https://github.com/df49b9cd/Odin/issues
- Review architecture docs: ../../docs/architecture/
