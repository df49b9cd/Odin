-- Odin Persistence Migration: Schedules (Down)

DROP TABLE IF EXISTS schedule_execution_history CASCADE;
DROP TABLE IF EXISTS workflow_schedules CASCADE;
