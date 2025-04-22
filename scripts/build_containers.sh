#!/bin/bash
# This script starts the docker containers for development or production.
# Usage: ./start_docker.sh [environment]
# environment: "dev" (default) or "prod"

ENV=${1:-dev}

if [ "$ENV" == "prod" ]; then
  echo "Starting production containers..."
  docker compose -f docker-compose.prod.yml up --build -d
else
  echo "Starting development containers..."
  docker compose -f docker-compose.dev.yml up --build -d
fi
