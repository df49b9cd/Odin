-- Odin Persistence Migration: Workflow Timers (Up)

CREATE TABLE IF NOT EXISTS workflow_timers (
    namespace_id UUID NOT NULL,
    workflow_id VARCHAR(255) NOT NULL,
    run_id UUID NOT NULL,
    timer_id VARCHAR(255) NOT NULL,
    fire_at TIMESTAMPTZ NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    timer_data JSONB,
    PRIMARY KEY (namespace_id, workflow_id, run_id, timer_id),
    FOREIGN KEY (namespace_id, workflow_id, run_id)
        REFERENCES workflow_executions(namespace_id, workflow_id, run_id)
        ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_workflow_timers_fire_at ON workflow_timers(fire_at);
CREATE INDEX IF NOT EXISTS idx_workflow_timers_workflow ON workflow_timers(namespace_id, workflow_id, run_id);

COMMENT ON TABLE workflow_timers IS 'Durable timers for workflow sleep and delayed execution';
COMMENT ON COLUMN workflow_timers.timer_id IS 'Unique timer identifier within the workflow';
COMMENT ON COLUMN workflow_timers.fire_at IS 'When the timer should fire';
COMMENT ON COLUMN workflow_timers.timer_data IS 'Additional timer metadata';
