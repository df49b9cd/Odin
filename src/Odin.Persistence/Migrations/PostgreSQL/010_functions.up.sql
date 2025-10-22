-- Odin Persistence Migration: Utility Functions (Up)

CREATE OR REPLACE FUNCTION update_updated_at_column()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER update_namespaces_updated_at BEFORE UPDATE ON namespaces
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

CREATE TRIGGER update_workflow_executions_updated_at BEFORE UPDATE ON workflow_executions
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

CREATE TRIGGER update_visibility_records_updated_at BEFORE UPDATE ON visibility_records
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

CREATE TRIGGER update_workflow_schedules_updated_at BEFORE UPDATE ON workflow_schedules
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

CREATE OR REPLACE FUNCTION calculate_shard_id(p_workflow_id VARCHAR, p_shard_count INTEGER DEFAULT 512)
RETURNS INTEGER AS $$
DECLARE
    hash_value BIGINT;
BEGIN
    hash_value := ABS(hashtext(p_workflow_id)::BIGINT);
    RETURN (hash_value % p_shard_count);
END;
$$ LANGUAGE plpgsql IMMUTABLE;

CREATE OR REPLACE FUNCTION cleanup_expired_tasks()
RETURNS INTEGER AS $$
DECLARE
    deleted_count INTEGER;
BEGIN
    DELETE FROM task_queues
    WHERE expiry_at IS NOT NULL AND expiry_at < NOW();
    
    GET DIAGNOSTICS deleted_count = ROW_COUNT;
    RETURN deleted_count;
END;
$$ LANGUAGE plpgsql;

CREATE OR REPLACE FUNCTION cleanup_expired_leases()
RETURNS INTEGER AS $$
DECLARE
    expired_count INTEGER;
BEGIN
    DELETE FROM task_queue_leases
    WHERE lease_expires_at < NOW();
    
    GET DIAGNOSTICS expired_count = ROW_COUNT;
    RETURN expired_count;
END;
$$ LANGUAGE plpgsql;

CREATE OR REPLACE FUNCTION get_next_task(
    p_namespace_id UUID,
    p_task_queue_name VARCHAR,
    p_task_queue_type VARCHAR,
    p_worker_identity VARCHAR,
    p_lease_duration_seconds INTEGER DEFAULT 60
)
RETURNS TABLE (
    task_id BIGINT,
    workflow_id VARCHAR,
    run_id UUID,
    task_data JSONB,
    lease_id UUID
) AS $$
DECLARE
    v_task_id BIGINT;
    v_workflow_id VARCHAR;
    v_run_id UUID;
    v_task_data JSONB;
    v_lease_id UUID;
    v_lease_expires_at TIMESTAMPTZ;
BEGIN
    v_lease_id := gen_random_uuid();
    v_lease_expires_at := NOW() + (p_lease_duration_seconds || ' seconds')::INTERVAL;
    
    SELECT tq.task_id, tq.workflow_id, tq.run_id, tq.task_data
    INTO v_task_id, v_workflow_id, v_run_id, v_task_data
    FROM task_queues tq
    WHERE tq.namespace_id = p_namespace_id
        AND tq.task_queue_name = p_task_queue_name
        AND tq.task_queue_type = p_task_queue_type
        AND (tq.expiry_at IS NULL OR tq.expiry_at > NOW())
        AND NOT EXISTS (
            SELECT 1 FROM task_queue_leases tql
            WHERE tql.namespace_id = tq.namespace_id
                AND tql.task_queue_name = tq.task_queue_name
                AND tql.task_queue_type = tq.task_queue_type
                AND tql.task_id = tq.task_id
                AND tql.lease_expires_at > NOW()
        )
    ORDER BY tq.scheduled_at ASC
    LIMIT 1
    FOR UPDATE SKIP LOCKED;
    
    IF v_task_id IS NOT NULL THEN
        INSERT INTO task_queue_leases (
            namespace_id, task_queue_name, task_queue_type, task_id,
            lease_id, worker_identity, lease_expires_at
        ) VALUES (
            p_namespace_id, p_task_queue_name, p_task_queue_type, v_task_id,
            v_lease_id, p_worker_identity, v_lease_expires_at
        );
        
        RETURN QUERY SELECT v_task_id, v_workflow_id, v_run_id, v_task_data, v_lease_id;
    END IF;
END;
$$ LANGUAGE plpgsql;

COMMENT ON FUNCTION calculate_shard_id IS 'Calculate consistent hash-based shard ID for workflow routing';
COMMENT ON FUNCTION cleanup_expired_tasks IS 'Remove expired tasks from task queues';
COMMENT ON FUNCTION cleanup_expired_leases IS 'Remove expired leases and make tasks available again';
COMMENT ON FUNCTION get_next_task IS 'Atomically get next available task and create a lease';
