-- Odin Persistence Migration: Task Queues (Up)

CREATE TABLE IF NOT EXISTS task_queues (
    namespace_id UUID NOT NULL REFERENCES namespaces(namespace_id) ON DELETE CASCADE,
    task_queue_name VARCHAR(255) NOT NULL,
    task_queue_type VARCHAR(50) NOT NULL CHECK (task_queue_type IN ('workflow', 'activity')),
    task_id BIGINT NOT NULL,
    workflow_id VARCHAR(255) NOT NULL,
    run_id UUID NOT NULL,
    scheduled_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    expiry_at TIMESTAMPTZ,
    task_data JSONB NOT NULL,
    partition_hash INTEGER NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (namespace_id, task_queue_name, task_queue_type, task_id)
);

CREATE INDEX IF NOT EXISTS idx_task_queues_partition ON task_queues(namespace_id, task_queue_name, task_queue_type, partition_hash, scheduled_at);
CREATE INDEX IF NOT EXISTS idx_task_queues_expiry ON task_queues(expiry_at) WHERE expiry_at IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_task_queues_workflow ON task_queues(namespace_id, workflow_id, run_id);
CREATE INDEX IF NOT EXISTS idx_task_queues_pending ON task_queues(namespace_id, task_queue_name, task_queue_type, scheduled_at)
    WHERE expiry_at IS NULL OR expiry_at > NOW();

COMMENT ON TABLE task_queues IS 'Pending workflow and activity tasks awaiting worker poll';
COMMENT ON COLUMN task_queues.task_queue_name IS 'Name of the task queue for worker routing';
COMMENT ON COLUMN task_queues.task_queue_type IS 'Type of task: workflow (decision) or activity';
COMMENT ON COLUMN task_queues.task_id IS 'Unique task identifier within the workflow';
COMMENT ON COLUMN task_queues.scheduled_at IS 'When the task was scheduled';
COMMENT ON COLUMN task_queues.expiry_at IS 'When the task should be removed (for timeouts)';
COMMENT ON COLUMN task_queues.partition_hash IS 'Hash for distributing tasks across matching service partitions';

CREATE TABLE IF NOT EXISTS task_queue_leases (
    namespace_id UUID NOT NULL,
    task_queue_name VARCHAR(255) NOT NULL,
    task_queue_type VARCHAR(50) NOT NULL,
    task_id BIGINT NOT NULL,
    lease_id UUID NOT NULL DEFAULT gen_random_uuid(),
    worker_identity VARCHAR(255) NOT NULL,
    leased_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    lease_expires_at TIMESTAMPTZ NOT NULL,
    heartbeat_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    attempt_count INTEGER NOT NULL DEFAULT 1,
    PRIMARY KEY (namespace_id, task_queue_name, task_queue_type, task_id, lease_id),
    FOREIGN KEY (namespace_id, task_queue_name, task_queue_type, task_id)
        REFERENCES task_queues(namespace_id, task_queue_name, task_queue_type, task_id)
        ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_task_queue_leases_worker ON task_queue_leases(worker_identity, lease_expires_at);
CREATE INDEX IF NOT EXISTS idx_task_queue_leases_expiry ON task_queue_leases(lease_expires_at);

COMMENT ON TABLE task_queue_leases IS 'Active leases for tasks being processed by workers';
COMMENT ON COLUMN task_queue_leases.worker_identity IS 'Identity of the worker that leased the task';
COMMENT ON COLUMN task_queue_leases.lease_expires_at IS 'When the lease expires if not heartbeated';
COMMENT ON COLUMN task_queue_leases.heartbeat_at IS 'Last heartbeat timestamp from worker';
COMMENT ON COLUMN task_queue_leases.attempt_count IS 'Number of times this task has been attempted';
