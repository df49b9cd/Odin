# Odin Persistence Layer – Database Migrations

This directory packages the SQL migrations that back Odin’s PostgreSQL persistence layer.  
Migrations follow the [`golang-migrate`](https://github.com/golang-migrate/migrate) naming convention (`<version>_<name>.up.sql` / `.down.sql`) so we can apply them with the official CLI or Docker image.

## Layout

```
Migrations/
├── PostgreSQL/
│   ├── 001_namespaces.up.sql
│   ├── 001_namespaces.down.sql
│   ├── 002_history_shards.up.sql
│   ├── …
│   └── 010_functions.down.sql
└── README.md
```

Each numbered pair represents a discrete change. The current sequence builds the entire Phase 1 schema surface (namespaces, shards, workflow state, history, task queues, visibility, timers, signals/queries, schedules, and helper functions/triggers).

## Applying migrations with `golang-migrate`

### Prerequisites

- PostgreSQL 14+
- The [`migrate` CLI](https://github.com/golang-migrate/migrate/tree/master/cmd/migrate) (or use the published Docker image `migrate/migrate`)

### Example: Apply migrations locally

```bash
# Build the connection string (adjust host/user/password/db as needed)
export ODIN_DB_URL="postgres://odin:odin@localhost:5432/odin?sslmode=disable"

# Run all outstanding migrations
migrate \
  -path ./src/Odin.Persistence/Migrations/PostgreSQL \
  -database "$ODIN_DB_URL" \
  up
```

### Using Docker

```bash
docker run --rm -v "$(pwd)/src/Odin.Persistence/Migrations/PostgreSQL:/migrations" migrate/migrate:latest \
  -path=/migrations \
  -database "$ODIN_DB_URL" \
  up
```

### Rolling back

```bash
migrate -path ./src/Odin.Persistence/Migrations/PostgreSQL -database "$ODIN_DB_URL" down 1
```

> ⚠️ The down scripts drop the corresponding objects. Use with care in shared environments.

## Generating new migrations

1. Increment the numeric prefix (e.g., `011_new_feature.up.sql` / `.down.sql`).
2. Keep forward migrations idempotent where possible (`CREATE TABLE IF NOT EXISTS`, `CREATE INDEX IF NOT EXISTS`, etc.).
3. Provide a down script that cleanly reverts the change (usually dropping the created objects).
4. Update `docs/PHASE1_PROGRESS.md` (Phase 1 workstreams) if the change alters roadmap scope.

## Legacy schema scripts

The previous `Schemas/PostgreSQL/*.sql` files have been superseded by this migration set. If you still need a full snapshot for documentation, use:

```bash
cat src/Odin.Persistence/Migrations/PostgreSQL/*_*.up.sql > /tmp/odin-schema.sql
```

This concatenates the current up migrations in order.
