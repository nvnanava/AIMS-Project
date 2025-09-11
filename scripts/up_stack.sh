#!/usr/bin/env bash
# up_stack.sh — Build & start the dev/prod stack, then wait for SQL to be healthy.
#
# USAGE:
#   ./scripts/up_stack.sh [dev|prod]
#
# QUICK DEBUG:
#   docker compose -f docker-compose.dev.yml up -d --build
#   docker compose ps
#   docker inspect -f '{{.State.Health.Status}}' sqlserver2022-dev
#   docker compose logs -f sqlserver-dev
#   docker compose logs -f web-dev
#
# NOTES:
# - We intentionally keep container_name=sqlserver2022-dev so older tools still work.
# - On Apple Silicon, Compose sets platform: linux/amd64 (Rosetta emulation). Ensure Docker Desktop
#   → Settings → Features in development → “Use Rosetta for x86/amd64 emulation” is enabled.

set -Eeuo pipefail

log()  { printf "\033[1;34m[up]\033[0m %s\n" "$*"; }
die()  { printf "\033[1;31m[up]\033[0m %s\n" "$*"; exit 1; }

# Windows Git Bash path quirk guard
if [[ "${MSYSTEM:-}" =~ MINGW|UCRT|MSYS ]] || [[ "${OSTYPE:-}" =~ msys|cygwin ]]; then
  export MSYS_NO_PATHCONV=1
fi

ENV="${1:-dev}"   # dev | prod

# Choose compose command
if docker compose version >/dev/null 2>&1; then
  COMPOSE=(docker compose)
elif command -v docker-compose >/dev/null 2>&1; then
  COMPOSE=(docker-compose)
else
  die "Neither 'docker compose' nor 'docker-compose' found."
fi

if [[ "$ENV" == "prod" ]]; then
  FILE="docker-compose.prod.yml"
  SQL_SVC="sqlserver-prod"
else
  FILE="docker-compose.dev.yml"
  SQL_SVC="sqlserver-dev"
fi

compose() { "${COMPOSE[@]}" -f "$FILE" "$@"; }

log "Starting $ENV containers (build + up)…"
compose up --build -d

# Resolve the real container ID for the service
SQL_CID="$(compose ps -q "$SQL_SVC")"
[[ -n "$SQL_CID" ]] || die "Could not resolve container ID for service '$SQL_SVC'."
log "Waiting for SQL container ($SQL_SVC → $SQL_CID) to be healthy…"
for i in {1..80}; do
  status="$(docker inspect -f '{{.State.Health.Status}}' "$SQL_CID" 2>/dev/null || echo starting)"
  log "  status: $status …"
  [[ "$status" == "healthy" ]] && { log "SQL is healthy."; exit 0; }
  sleep 2
done

die "SQL container never became healthy. See logs: docker compose -f $FILE logs -f $SQL_SVC"