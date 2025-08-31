# Docker Run information

To run this project, we have two sets of Dockerfiles and docker compose files: Dockerfile.dev + docker-compose.dev.yml, and Dockerfile.prod + docker-compose.prod.yml. The dev files allow for hot reloading via `dotnet watch run` (thus, the project isn't compiled) to allow for a faster and more streamlined experience. The prod files fully compile the project and run the production version of the application.

We have three scripts, which accept variables, to pick and choose which versions to run:

1. `./scripts/build_containers.sh`: When we first run the project, this is the first file that should be run, to fully set up the containers. This file accepts one variable: environment (values: `dev` (default) and `prod`).
  e.g., we can run the file with: `./scripts/build_containers.sh dev` or `./scripts/build_containers.sh prod`.

2. `./scripts/up_containers.sh`: Once the containers are built, we can use this to spin up the same containers again (or to spin them down). This file accepts two variables: environment (values: `dev` (default) and `prod`) and command (values: ` up`, `down`, `logs`, etc.).
  e.g., we can run the file with: `./scripts/build_containers.sh dev up`

3. `./scripts/stop_containers.sh`: Once the containers are built, we can use this to spin down the containers. This file accepts one variable: environment (values: `dev` (default) and `prod`).

1. `./scripts/build_containers.sh`: When we first run the project, this is the first file that should be run, to fully set up the containers. This file accepts one variable: environment (values: `dev` (default) and `prod`).
  e.g., we can run the file with: `./scripts/build_containers.sh dev` or `./scripts/build_containers.sh prod`.

2. `./scripts/up_containers.sh`: Once the containers are built, we can use this to spin up the same containers again (or to spin them down). This file accepts two variables: environment (values: `dev` (default) and `prod`) and command (values: ` up`, `down`, `logs`, etc.).
  e.g., we can run the file with: `./scripts/build_containers.sh dev up`

3. `./scripts/stop_containers.sh`: Once the containers are built, we can use this to spin down the containers. This file accepts one variable: environment (values: `dev` (default) and `prod`).
