-- Odin Persistence Migration: History Events (Up)

CREATE TABLE IF NOT EXISTS history_events (
    namespace_id UUID NOT NULL,
    workflow_id VARCHAR(255) NOT NULL,
    run_id UUID NOT NULL,
    event_id BIGINT NOT NULL,
    event_type VARCHAR(100) NOT NULL,
    event_timestamp TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    task_id BIGINT NOT NULL DEFAULT -1,
    version BIGINT NOT NULL DEFAULT 1,
    event_data JSONB NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (namespace_id, workflow_id, run_id, event_id),
    FOREIGN KEY (namespace_id, workflow_id, run_id) 
        REFERENCES workflow_executions(namespace_id, workflow_id, run_id) 
        ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_history_events_workflow_run ON history_events(namespace_id, workflow_id, run_id, event_id);
CREATE INDEX IF NOT EXISTS idx_history_events_type ON history_events(namespace_id, event_type, event_timestamp DESC);
CREATE INDEX IF NOT EXISTS idx_history_events_timestamp ON history_events(namespace_id, event_timestamp DESC);
CREATE INDEX IF NOT EXISTS idx_history_events_task ON history_events(namespace_id, workflow_id, run_id, task_id) WHERE task_id >= 0;
CREATE INDEX IF NOT EXISTS idx_history_events_data_gin ON history_events USING gin(event_data);

COMMENT ON TABLE history_events IS 'Immutable event log capturing complete workflow history';
COMMENT ON COLUMN history_events.event_id IS 'Monotonically increasing event sequence number within a run';
COMMENT ON COLUMN history_events.event_type IS 'Type of workflow event (WorkflowStarted, ActivityScheduled, etc.)';
COMMENT ON COLUMN history_events.task_id IS 'Associated workflow task ID for decision events';
COMMENT ON COLUMN history_events.version IS 'Event schema version for backward compatibility';
COMMENT ON COLUMN history_events.event_data IS 'Full event payload in JSON format';
