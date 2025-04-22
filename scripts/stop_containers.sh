#!/bin/bash
# This script stops the docker containers for development or production.
# Usage: ./stop_docker.sh [environment]
# environment: "dev" (default) or "prod"

ENV=${1:-dev}

if [ "$ENV" == "prod" ]; then
  echo "Stopping production containers..."
  docker compose -f docker-compose.prod.yml down
else
  echo "Stopping development containers..."
  docker compose -f docker-compose.dev.yml down
fi
