#!/bin/bash
# This is a generic wrapper for docker-compose commands using the appropriate compose file.
# Usage: ./docker_compose.sh [environment] [command] [options...]
# environment: "dev" (default) or "prod"
# command: any docker-compose command, e.g., up, down, logs, etc.

ENV=${1:-dev}
COMMAND=${2:-up}
shift 2

if [ "$ENV" == "prod" ]; then
  COMPOSE_FILE=docker-compose.prod.yml
else
  COMPOSE_FILE=docker-compose.dev.yml
fi

echo "Running: docker compose -f $COMPOSE_FILE $COMMAND $@"
docker compose -f $COMPOSE_FILE $COMMAND "$@"
