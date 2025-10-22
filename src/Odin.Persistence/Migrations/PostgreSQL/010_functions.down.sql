-- Odin Persistence Migration: Utility Functions (Down)

DROP TRIGGER IF EXISTS update_workflow_schedules_updated_at ON workflow_schedules;
DROP TRIGGER IF EXISTS update_visibility_records_updated_at ON visibility_records;
DROP TRIGGER IF EXISTS update_workflow_executions_updated_at ON workflow_executions;
DROP TRIGGER IF EXISTS update_namespaces_updated_at ON namespaces;

DROP FUNCTION IF EXISTS get_next_task(UUID, VARCHAR, VARCHAR, VARCHAR, INTEGER);
DROP FUNCTION IF EXISTS cleanup_expired_leases();
DROP FUNCTION IF EXISTS cleanup_expired_tasks();
DROP FUNCTION IF EXISTS cleanup_expired_tasks(TIMESTAMPTZ);
DROP FUNCTION IF EXISTS calculate_shard_id(VARCHAR, INTEGER);
DROP FUNCTION IF EXISTS update_updated_at_column();
