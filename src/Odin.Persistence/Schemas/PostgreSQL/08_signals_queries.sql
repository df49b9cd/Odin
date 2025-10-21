-- Odin Persistence Schema: Signals and Queries
-- PostgreSQL 14+ compatible

-- Buffered signals for workflows
CREATE TABLE IF NOT EXISTS workflow_signals (
    namespace_id UUID NOT NULL,
    workflow_id VARCHAR(255) NOT NULL,
    run_id UUID NOT NULL,
    signal_id UUID NOT NULL DEFAULT gen_random_uuid(),
    signal_name VARCHAR(255) NOT NULL,
    signal_input JSONB,
    received_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    processed BOOLEAN NOT NULL DEFAULT FALSE,
    PRIMARY KEY (namespace_id, workflow_id, run_id, signal_id),
    FOREIGN KEY (namespace_id, workflow_id, run_id)
        REFERENCES workflow_executions(namespace_id, workflow_id, run_id)
        ON DELETE CASCADE
);

CREATE INDEX idx_workflow_signals_pending ON workflow_signals(namespace_id, workflow_id, run_id, processed) WHERE NOT processed;
CREATE INDEX idx_workflow_signals_received ON workflow_signals(namespace_id, received_at DESC);

COMMENT ON TABLE workflow_signals IS 'Buffered signals waiting to be delivered to workflow execution';
COMMENT ON COLUMN workflow_signals.signal_name IS 'Name of the signal to deliver';
COMMENT ON COLUMN workflow_signals.signal_input IS 'Signal payload in JSON format';
COMMENT ON COLUMN workflow_signals.processed IS 'Whether the signal has been consumed by the workflow';

-- Query results for workflow queries
CREATE TABLE IF NOT EXISTS workflow_query_results (
    namespace_id UUID NOT NULL,
    workflow_id VARCHAR(255) NOT NULL,
    run_id UUID NOT NULL,
    query_id UUID NOT NULL DEFAULT gen_random_uuid(),
    query_name VARCHAR(255) NOT NULL,
    query_input JSONB,
    query_result JSONB,
    query_error TEXT,
    requested_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    completed_at TIMESTAMPTZ,
    PRIMARY KEY (namespace_id, workflow_id, run_id, query_id)
);

CREATE INDEX idx_workflow_query_results_workflow ON workflow_query_results(namespace_id, workflow_id, run_id, requested_at DESC);
CREATE INDEX idx_workflow_query_results_pending ON workflow_query_results(namespace_id, workflow_id, run_id) WHERE completed_at IS NULL;

COMMENT ON TABLE workflow_query_results IS 'Cached query results for workflow state interrogation';
COMMENT ON COLUMN workflow_query_results.query_name IS 'Name of the query handler';
COMMENT ON COLUMN workflow_query_results.query_input IS 'Query parameters in JSON format';
COMMENT ON COLUMN workflow_query_results.query_result IS 'Query response payload';
COMMENT ON COLUMN workflow_query_results.query_error IS 'Error message if query failed';
