-- Odin Persistence Schema: Namespaces
-- PostgreSQL 14+ compatible

CREATE TABLE IF NOT EXISTS namespaces (
    namespace_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    namespace_name VARCHAR(255) NOT NULL UNIQUE,
    description TEXT,
    owner_id VARCHAR(255),
    retention_days INTEGER NOT NULL DEFAULT 30,
    history_archival_enabled BOOLEAN NOT NULL DEFAULT FALSE,
    visibility_archival_enabled BOOLEAN NOT NULL DEFAULT FALSE,
    is_global_namespace BOOLEAN NOT NULL DEFAULT FALSE,
    cluster_config JSONB,
    data JSONB,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    status VARCHAR(50) NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'deprecated', 'deleted'))
);

-- Indexes
CREATE INDEX idx_namespaces_name ON namespaces(namespace_name);
CREATE INDEX idx_namespaces_status ON namespaces(status) WHERE status = 'active';
CREATE INDEX idx_namespaces_created_at ON namespaces(created_at DESC);

-- Comments
COMMENT ON TABLE namespaces IS 'Multi-tenant namespace isolation for workflows';
COMMENT ON COLUMN namespaces.retention_days IS 'Number of days to retain workflow history before cleanup';
COMMENT ON COLUMN namespaces.history_archival_enabled IS 'Enable automatic history archival to external storage';
COMMENT ON COLUMN namespaces.visibility_archival_enabled IS 'Enable automatic visibility record archival';
COMMENT ON COLUMN namespaces.cluster_config IS 'JSON configuration for namespace-specific cluster settings';
COMMENT ON COLUMN namespaces.data IS 'Additional metadata in JSON format';
