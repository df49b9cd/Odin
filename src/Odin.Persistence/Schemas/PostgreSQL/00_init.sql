-- Odin Persistence Schema: Master Migration Script
-- PostgreSQL 14+ compatible
-- 
-- This script applies all schema migrations in order.
-- Run this against a new database to initialize the Odin schema.

\echo 'Starting Odin schema initialization...'

\echo 'Creating namespaces table...'
\i 01_namespaces.sql

\echo 'Creating history shards table...'
\i 02_history_shards.sql

\echo 'Creating workflow executions table...'
\i 03_workflow_executions.sql

\echo 'Creating history events table...'
\i 04_history_events.sql

\echo 'Creating task queues tables...'
\i 05_task_queues.sql

\echo 'Creating visibility records tables...'
\i 06_visibility_records.sql

\echo 'Creating workflow timers table...'
\i 07_timers.sql

\echo 'Creating signals and queries tables...'
\i 08_signals_queries.sql

\echo 'Creating schedules tables...'
\i 09_schedules.sql

\echo 'Creating utility functions and triggers...'
\i 10_functions.sql

\echo 'Odin schema initialization complete!'
\echo 'Database is ready for workflow orchestration.'

-- Verify installation
SELECT 
    'Namespaces' AS table_name, COUNT(*) AS row_count FROM namespaces
UNION ALL
SELECT 'History Shards', COUNT(*) FROM history_shards
UNION ALL
SELECT 'Workflow Executions', COUNT(*) FROM workflow_executions
UNION ALL
SELECT 'History Events', COUNT(*) FROM history_events
UNION ALL
SELECT 'Task Queues', COUNT(*) FROM task_queues
UNION ALL
SELECT 'Visibility Records', COUNT(*) FROM visibility_records;

\echo ''
\echo 'Schema verification complete. Ready to start!'
