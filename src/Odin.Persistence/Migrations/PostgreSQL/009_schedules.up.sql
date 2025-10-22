-- Odin Persistence Migration: Schedules (Up)

CREATE TABLE IF NOT EXISTS workflow_schedules (
    schedule_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    namespace_id UUID NOT NULL REFERENCES namespaces(namespace_id) ON DELETE CASCADE,
    schedule_name VARCHAR(255) NOT NULL,
    workflow_type VARCHAR(255) NOT NULL,
    task_queue VARCHAR(255) NOT NULL,
    cron_expression VARCHAR(255),
    interval_seconds INTEGER,
    workflow_input JSONB,
    workflow_timeout_seconds INTEGER,
    retry_policy JSONB,
    memo JSONB,
    search_attributes JSONB,
    paused BOOLEAN NOT NULL DEFAULT FALSE,
    next_run_at TIMESTAMPTZ,
    last_run_at TIMESTAMPTZ,
    last_run_result VARCHAR(50),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by VARCHAR(255),
    CONSTRAINT unique_schedule_name UNIQUE (namespace_id, schedule_name),
    CONSTRAINT check_schedule_spec CHECK (
        (cron_expression IS NOT NULL AND interval_seconds IS NULL) OR
        (cron_expression IS NULL AND interval_seconds IS NOT NULL)
    )
);

CREATE INDEX IF NOT EXISTS idx_workflow_schedules_next_run ON workflow_schedules(next_run_at) WHERE NOT paused AND next_run_at IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_workflow_schedules_namespace ON workflow_schedules(namespace_id, paused);

COMMENT ON TABLE workflow_schedules IS 'Scheduled workflow executions (cron or interval-based)';
COMMENT ON COLUMN workflow_schedules.cron_expression IS 'Cron expression for periodic execution (mutually exclusive with interval)';
COMMENT ON COLUMN workflow_schedules.interval_seconds IS 'Interval in seconds for periodic execution (mutually exclusive with cron)';
COMMENT ON COLUMN workflow_schedules.workflow_input IS 'Default input payload for scheduled workflow runs';
COMMENT ON COLUMN workflow_schedules.paused IS 'Whether the schedule is currently paused';
COMMENT ON COLUMN workflow_schedules.next_run_at IS 'Next scheduled execution time';
COMMENT ON COLUMN workflow_schedules.last_run_at IS 'Last execution time';
COMMENT ON COLUMN workflow_schedules.last_run_result IS 'Result of last execution (completed, failed, etc.)';

CREATE TABLE IF NOT EXISTS schedule_execution_history (
    execution_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    schedule_id UUID NOT NULL REFERENCES workflow_schedules(schedule_id) ON DELETE CASCADE,
    namespace_id UUID NOT NULL,
    workflow_id VARCHAR(255) NOT NULL,
    run_id UUID NOT NULL,
    scheduled_at TIMESTAMPTZ NOT NULL,
    started_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    completed_at TIMESTAMPTZ,
    result VARCHAR(50),
    error_message TEXT,
    FOREIGN KEY (namespace_id, workflow_id, run_id)
        REFERENCES workflow_executions(namespace_id, workflow_id, run_id)
        ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_schedule_execution_history_schedule ON schedule_execution_history(schedule_id, started_at DESC);
CREATE INDEX IF NOT EXISTS idx_schedule_execution_history_workflow ON schedule_execution_history(namespace_id, workflow_id, run_id);

COMMENT ON TABLE schedule_execution_history IS 'Historical record of scheduled workflow executions';
