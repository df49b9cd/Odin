-- Odin Persistence Migration: Workflow Executions (Up)

CREATE TABLE IF NOT EXISTS workflow_executions (
    namespace_id UUID NOT NULL REFERENCES namespaces(namespace_id) ON DELETE CASCADE,
    workflow_id VARCHAR(255) NOT NULL,
    run_id UUID NOT NULL DEFAULT gen_random_uuid(),
    workflow_type VARCHAR(255) NOT NULL,
    task_queue VARCHAR(255) NOT NULL,
    workflow_state VARCHAR(50) NOT NULL DEFAULT 'running' CHECK (workflow_state IN ('running', 'completed', 'failed', 'canceled', 'terminated', 'continued_as_new', 'timed_out')),
    execution_state BYTEA,
    next_event_id BIGINT NOT NULL DEFAULT 1,
    last_processed_event_id BIGINT NOT NULL DEFAULT 0,
    workflow_timeout_seconds INTEGER,
    run_timeout_seconds INTEGER,
    task_timeout_seconds INTEGER,
    retry_policy JSONB,
    cron_schedule VARCHAR(255),
    parent_workflow_id VARCHAR(255),
    parent_run_id UUID,
    initiated_id BIGINT,
    completion_event_id BIGINT,
    memo JSONB,
    search_attributes JSONB,
    auto_reset_points JSONB,
    started_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    completed_at TIMESTAMPTZ,
    last_updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    shard_id INTEGER NOT NULL,
    version BIGINT NOT NULL DEFAULT 1,
    PRIMARY KEY (namespace_id, workflow_id, run_id)
);

CREATE INDEX IF NOT EXISTS idx_workflow_executions_namespace_state ON workflow_executions(namespace_id, workflow_state);
CREATE INDEX IF NOT EXISTS idx_workflow_executions_task_queue ON workflow_executions(namespace_id, task_queue, workflow_state);
CREATE INDEX IF NOT EXISTS idx_workflow_executions_workflow_type ON workflow_executions(namespace_id, workflow_type);
CREATE INDEX IF NOT EXISTS idx_workflow_executions_shard ON workflow_executions(shard_id, workflow_state);
CREATE INDEX IF NOT EXISTS idx_workflow_executions_started_at ON workflow_executions(namespace_id, started_at DESC);
CREATE INDEX IF NOT EXISTS idx_workflow_executions_parent ON workflow_executions(namespace_id, parent_workflow_id, parent_run_id) WHERE parent_workflow_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_workflow_executions_active ON workflow_executions(namespace_id, workflow_id) 
    WHERE workflow_state IN ('running', 'continued_as_new');

COMMENT ON TABLE workflow_executions IS 'Mutable workflow execution state tracking current run status';
COMMENT ON COLUMN workflow_executions.execution_state IS 'Serialized workflow execution state for replay';
COMMENT ON COLUMN workflow_executions.next_event_id IS 'Next event ID to be assigned in the history';
COMMENT ON COLUMN workflow_executions.last_processed_event_id IS 'Last history event processed by worker';
COMMENT ON COLUMN workflow_executions.search_attributes IS 'Key-value pairs for advanced visibility search';
COMMENT ON COLUMN workflow_executions.auto_reset_points IS 'Checkpoint events where workflow can be reset';
COMMENT ON COLUMN workflow_executions.shard_id IS 'History shard that owns this workflow execution';
COMMENT ON COLUMN workflow_executions.version IS 'Optimistic concurrency control version';
