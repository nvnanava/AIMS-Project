# Short documentation for docker compose scripts

- Use command ./scripts/docker_compose.sh for creating the container
  initially
    - contains three commands that starts the container in detached mode (in background),
      prints list of containers in project, and shows the logs in real time
- Use command ./scripts/start_docker.sh to stop the container
    - contains one command that stops the container without deleting it
- Use command ./scripts/start_docker.sh to restart the container
    - contains one command that restarts the container
    - retains information from last session


