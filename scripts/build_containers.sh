#!/bin/bash
# Builds & starts containers (dev/prod), waits for SQL health,
# ensures AIMS DB exists, then (in dev) runs EF migrations inside the web container.
# Usage: ./scripts/build_containers.sh [dev|prod]

set -euo pipefail

# --- Keep host VS Code in sync with container's .NET SDK ---
SDK_VER="9.0.304"  # keep in sync with your Docker image
if [ ! -f ./global.json ] || ! grep -q "\"version\":\s*\"$SDK_VER\"" ./global.json; then
  echo "Writing global.json (SDK $SDK_VER) so host VS Code uses the same SDK..."
  cat > ./global.json <<EOF
{
  "sdk": {
    "version": "$SDK_VER",
    "rollForward": "latestFeature",
    "allowPrerelease": false
  }
}
EOF
fi

# Clean host build artifacts that can confuse analyzers
rm -rf ./AIMS/bin ./AIMS/obj 2>/dev/null || true

ENV=${1:-dev}

# Choose compose file + service names
if [ "$ENV" = "prod" ]; then
  echo "Starting production containers..."
  COMPOSE_FILE="docker-compose.prod.yml"
  DB_SVC="sqlserver-prod"
  WEB_SVC="web-prod"
  DOTNET_ENV="Production"
else
  echo "Starting development containers..."
  COMPOSE_FILE="docker-compose.dev.yml"
  DB_SVC="sqlserver-dev"
  WEB_SVC="web-dev"
  DOTNET_ENV="Development"
fi

# Helper to run compose with the chosen file
compose() { docker compose -f "$COMPOSE_FILE" "$@"; }

# Bring services up
compose up --build -d

# --- Resolve container id for SQL (DON'T rely on service name directly) ---
SQL_CID="$(compose ps -q "$DB_SVC" || true)"
if [ -z "$SQL_CID" ]; then
  echo "ERROR: Could not resolve container id for service '$DB_SVC'."
  compose ps
  exit 1
fi

# --- Wait for SQL to be healthy ---
echo "Waiting for SQL Server container ($DB_SVC → $SQL_CID) to be healthy..."
for i in {1..80}; do
  status="$(docker inspect -f '{{.State.Health.Status}}' "$SQL_CID" 2>/dev/null || echo starting)"
  echo "  status: $status ..."
  [ "$status" = "healthy" ] && { echo "SQL Server is healthy."; break; }
  sleep 2
  if [ $i -eq 80 ]; then
    echo "ERROR: SQL container never became healthy."
    compose logs "$DB_SVC"
    exit 1
  fi
done

# --- Ensure AIMS DB exists ---
SA_USER="sa"
SA_PASS='StrongP@ssword!'   # keep single quotes; "!" breaks double-quoted args
echo "Ensuring 'AIMS' database exists..."
docker exec -i "$SQL_CID" /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U "$SA_USER" -P "$SA_PASS" -No -C \
  -Q "IF DB_ID('AIMS') IS NULL CREATE DATABASE [AIMS];"
echo "Confirmed 'AIMS' database is present."

# --- DEV ONLY: run EF migrations inside the web container (use compose exec) ---
if [ "$ENV" != "prod" ]; then
  echo "Running EF migrations in '$WEB_SVC'…"
  # Ensure the web service is actually running
  WEB_CID="$(compose ps -q "$WEB_SVC" || true)"
  if [ -z "$WEB_CID" ]; then
    echo "WARNING: '$WEB_SVC' not running; skipping EF migration."
  else
    compose exec -T "$WEB_SVC" bash -lc "
      set -e
      cd /src/AIMS
      # First-run guard so dotnet-ef has deps
      if [ ! -f bin/Debug/net9.0/AIMS.deps.json ]; then
        echo '[ef] deps.json missing — building once...'
        dotnet build -c Debug /p:UseSharedCompilation=false
      fi
      export DOTNET_ENVIRONMENT='${DOTNET_ENV}'
      dotnet ef database update -c AimsDbContext --no-build
    "
    echo "EF migrations completed."
  fi
else
  echo "Production: skipping inline EF migrations."
fi

echo "All set. Containers are up, DB exists, and (dev) migrations have been applied."