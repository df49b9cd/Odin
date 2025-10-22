-- Odin Persistence Migration: History Shards (Up)

CREATE TABLE IF NOT EXISTS history_shards (
    shard_id INTEGER PRIMARY KEY CHECK (shard_id >= 0 AND shard_id < 512),
    owner_identity VARCHAR(255),
    lease_expires_at TIMESTAMPTZ,
    acquired_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    last_heartbeat TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    range_start BIGINT NOT NULL,
    range_end BIGINT NOT NULL,
    stolen_since_renew INTEGER NOT NULL DEFAULT 0,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    data JSONB,
    CONSTRAINT check_shard_range CHECK (range_end > range_start)
);

CREATE INDEX IF NOT EXISTS idx_history_shards_owner ON history_shards(owner_identity);
CREATE INDEX IF NOT EXISTS idx_history_shards_expiry ON history_shards(lease_expires_at);

COMMENT ON TABLE history_shards IS 'Shard metadata for distributing workflow execution across multiple history service instances';
COMMENT ON COLUMN history_shards.shard_id IS 'Shard identifier (0-511 by default, supports 512 shards)';
COMMENT ON COLUMN history_shards.owner_identity IS 'Identity of the history service instance that owns this shard';
COMMENT ON COLUMN history_shards.lease_expires_at IS 'Timestamp when current lease expires';
COMMENT ON COLUMN history_shards.acquired_at IS 'Timestamp when the current owner acquired the shard';
COMMENT ON COLUMN history_shards.last_heartbeat IS 'Timestamp of the most recent heartbeat from the owner';
COMMENT ON COLUMN history_shards.range_start IS 'Start of the workflow ID hash range for this shard';
COMMENT ON COLUMN history_shards.range_end IS 'End of the workflow ID hash range for this shard';
COMMENT ON COLUMN history_shards.stolen_since_renew IS 'Counter tracking shard ownership changes';

INSERT INTO history_shards (shard_id, range_start, range_end)
SELECT 
    shard_id,
    shard_id * (9223372036854775807::BIGINT / 512),
    (shard_id + 1) * (9223372036854775807::BIGINT / 512) - 1
FROM generate_series(0, 511) AS shard_id
ON CONFLICT (shard_id) DO NOTHING;
