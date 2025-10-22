#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MIGRATIONS_DIR="$(cd "${SCRIPT_DIR}/../src/Odin.Persistence/Migrations/PostgreSQL" && pwd)"

if ! command -v docker >/dev/null 2>&1; then
  echo "docker is required to run migrations via migrate/migrate container." >&2
  exit 1
fi

if [[ -z "${ODIN_DB_URL:-}" ]]; then
  echo "Set ODIN_DB_URL to a valid PostgreSQL connection string before running this script." >&2
  echo "Example: export ODIN_DB_URL=\"postgres://odin:odin@localhost:5432/odin?sslmode=disable\"" >&2
  exit 1
fi

COMMAND="${1:-up}"
shift || true

docker run --rm \
  -v "${MIGRATIONS_DIR}:/migrations" \
  migrate/migrate:latest \
  -path=/migrations \
  -database "${ODIN_DB_URL}" \
  "${COMMAND}" "$@"
