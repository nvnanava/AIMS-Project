#!/bin/bash
# Builds & starts containers (dev/prod), waits for SQL health,
# ensures AIMS DB exists, then (in dev) runs EF migrations inside the web container.
# Usage: ./scripts/build_containers.sh [environment]
#   environment: "dev" (default) or "prod"

set -euo pipefail

ENV=${1:-dev}

if [ "$ENV" == "prod" ]; then
  echo "Starting production containers..."
  docker compose -f docker-compose.prod.yml up --build -d
  DB_SVC="sqlserver2022-prod"
  COMPOSE_FILE="docker-compose.prod.yml"
  DOTNET_ENV="Production"
  WEB_IMAGE_MATCH="aims-project-web-prod"
else
  echo "Starting development containers..."
  docker compose -f docker-compose.dev.yml up --build -d
  DB_SVC="sqlserver2022-dev"
  COMPOSE_FILE="docker-compose.dev.yml"
  DOTNET_ENV="Development"
  WEB_IMAGE_MATCH="aims-project-web-dev"
fi

# --- SQL credentials (must match docker-compose) ---
SA_USER="sa"
SA_PASS='StrongP@ssword!'   # keep single quotes; "!" breaks double-quoted args

# --- Wait for SQL to be healthy ---
echo "Waiting for SQL Server container ($DB_SVC) to be healthy..."
until [[ "$(docker inspect -f '{{.State.Health.Status}}' "${DB_SVC}" 2>/dev/null || echo starting)" == "healthy" ]]; do
  STATUS="$(docker inspect -f '{{.State.Health.Status}}' "${DB_SVC}" 2>/dev/null || echo starting)"
  echo "  status: ${STATUS} ..."
  sleep 3
done
echo "SQL Server is healthy."

# --- Ensure AIMS DB exists ---
echo "Ensuring 'AIMS' database exists..."
docker exec -i "${DB_SVC}" /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U "${SA_USER}" -P "${SA_PASS}" -No -C \
  -Q "IF DB_ID('AIMS') IS NULL CREATE DATABASE AIMS;"
echo "Confirmed 'AIMS' database is present."

# --- DEV ONLY: run EF migrations inside the web container ---
if [ "$ENV" != "prod" ]; then
  echo "Locating web container for EF migrations..."
  # Try to find the dev web container by image/name pattern
  WEB_CNAME="$(docker ps --format '{{.Names}} {{.Image}}' | awk -v pat="$WEB_IMAGE_MATCH" '$0 ~ pat {print $1; exit}')"

  if [ -z "${WEB_CNAME:-}" ]; then
    echo "WARNING: Could not find a running web container matching '$WEB_IMAGE_MATCH'."
    echo "Skipping inline EF migration. The app will try to migrate on startup."
  else
    echo "Running EF migrations inside '${WEB_CNAME}'..."
    # Path inside container where the solution is mounted (based on our images/logs)
    docker exec -i "${WEB_CNAME}" bash -lc "
      set -e
      cd /src/AIMS
      DOTNET_ENVIRONMENT=${DOTNET_ENV} dotnet ef database update
    "
    echo "EF migrations completed."
  fi
else
  echo "Production: skipping inline EF migrations."
  echo "Ensure your deployment strategy runs migrations out-of-band or via app startup policy."
fi

echo "All set. Containers are up, DB exists, and (dev) migrations have been applied."