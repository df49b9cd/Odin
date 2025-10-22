-- Odin Persistence Migration: Visibility Records (Up)

CREATE TABLE IF NOT EXISTS visibility_records (
    namespace_id UUID NOT NULL REFERENCES namespaces(namespace_id) ON DELETE CASCADE,
    workflow_id VARCHAR(255) NOT NULL,
    run_id UUID NOT NULL,
    workflow_type VARCHAR(255) NOT NULL,
    task_queue VARCHAR(255) NOT NULL,
    workflow_state VARCHAR(50) NOT NULL,
    start_time TIMESTAMPTZ NOT NULL,
    execution_time TIMESTAMPTZ NOT NULL,
    close_time TIMESTAMPTZ,
    status VARCHAR(50),
    history_length BIGINT NOT NULL DEFAULT 0,
    execution_duration_ms BIGINT,
    state_transition_count INTEGER NOT NULL DEFAULT 0,
    memo JSONB,
    search_attributes JSONB,
    parent_workflow_id VARCHAR(255),
    parent_run_id UUID,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (namespace_id, workflow_id, run_id),
    FOREIGN KEY (namespace_id, workflow_id, run_id)
        REFERENCES workflow_executions(namespace_id, workflow_id, run_id)
        ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_visibility_namespace_state ON visibility_records(namespace_id, workflow_state, start_time DESC);
CREATE INDEX IF NOT EXISTS idx_visibility_namespace_type ON visibility_records(namespace_id, workflow_type, start_time DESC);
CREATE INDEX IF NOT EXISTS idx_visibility_task_queue ON visibility_records(namespace_id, task_queue, workflow_state);
CREATE INDEX IF NOT EXISTS idx_visibility_close_time ON visibility_records(namespace_id, close_time DESC) WHERE close_time IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_visibility_execution_time ON visibility_records(namespace_id, execution_time DESC);
CREATE INDEX IF NOT EXISTS idx_visibility_parent ON visibility_records(namespace_id, parent_workflow_id, parent_run_id) WHERE parent_workflow_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_visibility_search_attrs ON visibility_records USING gin(search_attributes);
CREATE INDEX IF NOT EXISTS idx_visibility_list_workflows ON visibility_records(namespace_id, workflow_state, workflow_type, start_time DESC);

COMMENT ON TABLE visibility_records IS 'Searchable workflow metadata for visibility queries and listing';
COMMENT ON COLUMN visibility_records.workflow_state IS 'Current state of the workflow execution';
COMMENT ON COLUMN visibility_records.start_time IS 'When the workflow was started';
COMMENT ON COLUMN visibility_records.execution_time IS 'Actual execution start time (may differ from start_time for scheduled workflows)';
COMMENT ON COLUMN visibility_records.close_time IS 'When the workflow completed, failed, or was terminated';
COMMENT ON COLUMN visibility_records.history_length IS 'Total number of events in workflow history';
COMMENT ON COLUMN visibility_records.execution_duration_ms IS 'Total execution time in milliseconds';
COMMENT ON COLUMN visibility_records.state_transition_count IS 'Number of state transitions for debugging';
COMMENT ON COLUMN visibility_records.search_attributes IS 'Custom key-value pairs for advanced search';

CREATE TABLE IF NOT EXISTS workflow_tags (
    namespace_id UUID NOT NULL,
    workflow_id VARCHAR(255) NOT NULL,
    run_id UUID NOT NULL,
    tag_key VARCHAR(255) NOT NULL,
    tag_value TEXT,
    PRIMARY KEY (namespace_id, workflow_id, run_id, tag_key),
    FOREIGN KEY (namespace_id, workflow_id, run_id)
        REFERENCES workflow_executions(namespace_id, workflow_id, run_id)
        ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_workflow_tags_key_value ON workflow_tags(namespace_id, tag_key, tag_value);

COMMENT ON TABLE workflow_tags IS 'Tag-based workflow classification for filtering and grouping';
