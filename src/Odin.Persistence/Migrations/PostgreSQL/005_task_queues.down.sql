-- Odin Persistence Migration: Task Queues (Down)

DROP TABLE IF EXISTS task_queue_leases CASCADE;
DROP TABLE IF EXISTS task_queues CASCADE;
