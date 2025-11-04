#!/usr/bin/env bash
# db_ready.sh — Ensure (or reseed) the AIMS DB and (in dev) run EF migrations.
#
# USAGE:
#   ./scripts/db_ready.sh [dev|prod] [ensure|reseed]
#     dev|prod  -> which compose file to use (default: dev)
#     ensure    -> create DB if missing (default)
#     reseed    -> DROP and CREATE DB AIMS (DANGEROUS in prod)
#     --hard    -> (optional) with reseed, fully tear down stack and volumes first
#
# QUICK DEBUG:
#   docker compose ps
#   docker inspect -f '{{.State.Health.Status}}' sqlserver2022-dev
#   docker compose logs -f sqlserver-dev
#   docker compose logs -f web-dev
#   docker exec -it sqlserver2022-dev /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'StrongP@ssword!' -No -C -Q "SELECT name FROM sys.databases"
#
# NOTES:
# - ConnectionStrings__Default and __DockerConnection both point at the same DSN in compose.
# - EF migrations run inside the web container so they use the same env/CS.

set -Eeuo pipefail

info(){ printf "\033[1;34m[db]\033[0m %s\n" "$*"; }
warn(){ printf "\033[1;33m[db]\033[0m %s\n" "$*"; }
fail(){ printf "\033[1;31m[db]\033[0m %s\n" "$*"; exit 1; }

# Windows Git Bash path quirk guard
if [[ "${MSYSTEM:-}" =~ MINGW|UCRT|MSYS ]] || [[ "${OSTYPE:-}" =~ msys|cygwin ]]; then
  export MSYS_NO_PATHCONV=1
fi

ENV="${1:-dev}"        # dev | prod
ACTION="${2:-ensure}"  # ensure | reseed
HARD_FLAG="${3:-}"     # optional: --hard | hard | --volumes | -v

# ---- Seed flags (shared) ----
SEED_MODE="basic"
SEED_DIR="./seed"
ALLOW_PROD_SEED="false"

# We've already parsed: ENV ACTION HARD_FLAG
# So start consuming from $4 onward:
shift 3 || true

while [[ $# -gt 0 ]]; do
  case "$1" in
    --seed-mode)   SEED_MODE="${2:-$SEED_MODE}"; shift 2;;
    --seed-dir)    SEED_DIR="${2:-$SEED_DIR}";   shift 2;;
    --allow-prod-seed) ALLOW_PROD_SEED="true";   shift;;
    *) break;;
  esac
done

export AIMS_SEED_MODE="$SEED_MODE"
export AIMS_SEED_DIR="$SEED_DIR"
export AIMS_ALLOW_PROD_SEED="$ALLOW_PROD_SEED"

# Compose command
if docker compose version >/dev/null 2>&1; then
  COMPOSE=(docker compose)
elif command -v docker-compose >/dev/null 2>&1; then
  COMPOSE=(docker-compose)
else
  fail "Neither 'docker compose' nor 'docker-compose' found."
fi

if [[ "$ENV" == "prod" ]]; then
  FILE="docker-compose.prod.yml"
  SQL_SVC="sqlserver-prod"
  WEB_SVC="web-prod"
  DOTNET_ENV="Production"
else
  FILE="docker-compose.dev.yml"
  SQL_SVC="sqlserver-dev"
  WEB_SVC="web-dev"
  DOTNET_ENV="Development"
fi

SA_USER="sa"
SA_PASS='StrongP@ssword!'   # keep single quotes to protect '!'

compose() { "${COMPOSE[@]}" -f "$FILE" "$@"; }

# Optional hard reseed: fully tear down stack (including volumes) and start SQL fresh
if [[ "$ACTION" == "reseed" ]] && [[ "$HARD_FLAG" =~ ^(--hard|hard|--volumes|-v)$ ]]; then
  warn "Hard reseed requested — bringing stack down and removing volumes…"
  compose down --volumes --remove-orphans || true
  info "Starting SQL service fresh…"
  compose up -d "$SQL_SVC"
fi

# Quick health check before we proceed
SQL_CID="$(compose ps -q "$SQL_SVC")"
[[ -n "$SQL_CID" ]] || fail "Could not resolve container ID for service '$SQL_SVC'."
status="$(docker inspect -f '{{.State.Health.Status}}' "$SQL_CID" 2>/dev/null || echo "unknown")"
[[ "$status" == "healthy" ]] || warn "SQL is not healthy yet (status=$status). Proceeding with retries…"

run_sql(){
  local q="$1"
  for t in {1..8}; do
    if docker exec -i "$SQL_CID" /opt/mssql-tools18/bin/sqlcmd \
      -S localhost -U "$SA_USER" -P "$SA_PASS" -No -C -Q "$q" >/dev/null; then
      return 0
    fi
    warn "sqlcmd try $t failed; retrying in 2s…"
    sleep 2
  done
  return 1
}

if [[ "$ACTION" == "reseed" ]]; then
  warn "Dropping and recreating database 'AIMS'..."
  run_sql "IF DB_ID('AIMS') IS NOT NULL DROP DATABASE [AIMS]; CREATE DATABASE [AIMS];" \
    || fail "DB reseed failed."
  info "Database reseeded."
else
  info "Ensuring database 'AIMS' exists..."
  run_sql "IF DB_ID('AIMS') IS NULL CREATE DATABASE [AIMS];" \
    || fail "Ensure DB failed."
  info "Database present."
fi

# If we did a hard reseed in dev, make sure the web container is up before migrations
if [[ "$ENV" != "prod" ]] && [[ "$ACTION" == "reseed" ]] && [[ "$HARD_FLAG" =~ ^(--hard|hard|--volumes|-v)$ ]]; then
  info "Starting web service after hard reseed…"
  compose up -d "$WEB_SVC"
  # brief grace period to let Kestrel boot before exec
  sleep 5
fi

# Dev only: run EF migrations in the web container
if [[ "$ENV" != "prod" ]]; then
  # Make sure the web service is running
  WEB_CID="$(compose ps -q "$WEB_SVC" || true)"
  if [[ -z "$WEB_CID" ]]; then
    warn "Web container not running; skipping EF migration."
    exit 0
  fi

  info "Running EF migrations in '${WEB_SVC}'…"
  compose exec -T "$WEB_SVC" bash -lc "
    set -euo pipefail
    cd /src/AIMS

    # First-run guard so dotnet-ef can resolve deps
    if [ ! -f bin/Debug/net9.0/AIMS.deps.json ]; then
      echo '[ef] deps.json missing — building once...'
      dotnet build -c Debug /p:UseSharedCompilation=false
    fi

    export DOTNET_ENVIRONMENT='${DOTNET_ENV}'
    export AIMS_SEED_MODE='${SEED_MODE}'
    export AIMS_SEED_DIR='${SEED_DIR}'
    export AIMS_ALLOW_PROD_SEED='${ALLOW_PROD_SEED}'

    # If there are no migrations yet, scaffold the baseline one.
    if ! dotnet ef migrations list -c AimsDbContext --no-build 2>/dev/null | grep -Eq '^[0-9]{14}_.+'; then
      echo '[ef] No migrations detected — creating InitialCreate...'
      # Ensure we don't have a stale empty folder from a previous run
      [ -d Migrations ] && rmdir Migrations 2>/dev/null || true
      dotnet ef migrations add InitialCreate -c AimsDbContext --no-build
    fi

    # Apply migrations
    dotnet ef database update -c AimsDbContext --no-build
  " && info "EF migrations completed."
else
  info "Prod: skipping EF migrations (run out-of-band or via app policy)."
fi