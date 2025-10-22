-- Odin Persistence Migration: Namespaces (Down)
-- Drops namespace metadata artifacts.

DROP TABLE IF EXISTS namespaces CASCADE;
