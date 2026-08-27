#!/usr/bin/env bash
# Local runner for HERM-MAPPER-APP.
#
#   ./run.sh start [port]            - build and start the app with dotnet (default runtime)
#   ./run.sh dotnet start [port]     - same, explicit
#   ./run.sh docker start [env]      - build and start the container stack (env: example|prod|<path>)
#   ./run.sh [dotnet|docker] stop    - stop that runtime
#   ./run.sh [dotnet|docker] restart - stop, then start
#   ./run.sh [dotnet|docker] status  - show whether it is running
#   ./run.sh [dotnet|docker] logs [-f|lines]
#   ./run.sh [dotnet|docker] clean   - stop and remove build output / containers and volumes
#
# Without a runtime word the command applies to dotnet, except `status`, which
# reports both.
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$ROOT_DIR/src/HERM-MAPPER-APP/HERM-MAPPER-APP.csproj"
RUN_DIR="$ROOT_DIR/.run"
PID_FILE="$RUN_DIR/app.pid"
LOG_FILE="$RUN_DIR/app.log"
PORT_FILE="$RUN_DIR/app.port"
ENV_NAME_FILE="$RUN_DIR/docker.env"
COMPOSE_FILE="$ROOT_DIR/docker/docker-compose.yml"
DEFAULT_PORT="5143"
DEFAULT_DOCKER_ENV="example"

usage() {
    sed -n '2,14p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
    exit "${1:-1}"
}

# ---------------------------------------------------------------- dotnet mode

dotnet_is_running() {
    [[ -f "$PID_FILE" ]] || return 1
    local pid
    pid="$(cat "$PID_FILE")"
    [[ -n "$pid" ]] && kill -0 "$pid" 2>/dev/null
}

dotnet_url() {
    local port="$DEFAULT_PORT"
    [[ -f "$PORT_FILE" ]] && port="$(cat "$PORT_FILE")"
    echo "http://localhost:$port"
}

dotnet_start() {
    local port="${1:-$DEFAULT_PORT}"

    if dotnet_is_running; then
        echo "Already running (pid $(cat "$PID_FILE")) on $(dotnet_url)"
        return 0
    fi

    mkdir -p "$RUN_DIR"
    echo "Building..."
    dotnet build "$PROJECT" -c Debug --nologo -v minimal

    echo "Starting on http://localhost:$port ..."
    ASPNETCORE_ENVIRONMENT=Development \
    ASPNETCORE_URLS="http://localhost:$port" \
    DOTNET_ENVIRONMENT=Development \
        nohup dotnet run --project "$PROJECT" -c Debug --no-build --no-launch-profile \
        >"$LOG_FILE" 2>&1 &

    echo $! >"$PID_FILE"
    echo "$port" >"$PORT_FILE"

    for _ in $(seq 1 60); do
        if curl -fsS -o /dev/null "http://localhost:$port/" 2>/dev/null; then
            echo "Up: http://localhost:$port (pid $(cat "$PID_FILE"), log $LOG_FILE)"
            return 0
        fi
        if ! dotnet_is_running; then
            echo "Failed to start. Last log lines:" >&2
            tail -n 40 "$LOG_FILE" >&2 || true
            return 1
        fi
        sleep 1
    done

    echo "Started but no HTTP response yet. Check: ./run.sh logs" >&2
    return 1
}

dotnet_stop() {
    if ! dotnet_is_running; then
        echo "dotnet: not running."
        rm -f "$PID_FILE"
        return 0
    fi

    local pid
    pid="$(cat "$PID_FILE")"
    echo "Stopping pid $pid ..."
    pkill -TERM -P "$pid" 2>/dev/null || true
    kill -TERM "$pid" 2>/dev/null || true

    for _ in $(seq 1 20); do
        dotnet_is_running || break
        sleep 0.5
    done

    if dotnet_is_running; then
        pkill -KILL -P "$pid" 2>/dev/null || true
        kill -KILL "$pid" 2>/dev/null || true
    fi

    rm -f "$PID_FILE"
    echo "Stopped."
}

dotnet_status() {
    if dotnet_is_running; then
        echo "dotnet: running (pid $(cat "$PID_FILE")) on $(dotnet_url)"
    else
        echo "dotnet: not running."
    fi
}

dotnet_logs() {
    [[ -f "$LOG_FILE" ]] || { echo "No log yet at $LOG_FILE"; return 0; }
    if [[ "${1:-}" == "-f" ]]; then
        tail -f "$LOG_FILE"
    else
        tail -n "${1:-200}" "$LOG_FILE"
    fi
}

dotnet_clean() {
    dotnet_stop
    echo "Removing build output and run artefacts..."
    dotnet clean "$PROJECT" -c Debug --nologo -v minimal || true
    rm -rf "$ROOT_DIR/src/HERM-MAPPER-APP/bin" "$ROOT_DIR/src/HERM-MAPPER-APP/obj" "$RUN_DIR"
    echo "Clean."
}

# ---------------------------------------------------------------- docker mode

# Accepts a short name (example, prod) or a path to an env file.
docker_env_file() {
    local name="${1:-}"

    if [[ -z "$name" ]]; then
        name="$([[ -f "$ENV_NAME_FILE" ]] && cat "$ENV_NAME_FILE" || echo "$DEFAULT_DOCKER_ENV")"
    fi

    local candidate="$name"
    [[ -f "$candidate" ]] || candidate="$ROOT_DIR/docker/.env.$name"

    if [[ ! -f "$candidate" ]]; then
        echo "Env file not found: $candidate" >&2
        echo "Copy docker/.env.example to docker/.env.prod, or pass a path." >&2
        return 1
    fi

    echo "$candidate"
}

compose() {
    local env_file="$1"
    shift
    docker compose --project-directory "$ROOT_DIR/docker" -f "$COMPOSE_FILE" --env-file "$env_file" "$@"
}

docker_start() {
    local env_file
    env_file="$(docker_env_file "${1:-}")" || return 1

    mkdir -p "$RUN_DIR"
    basename "$env_file" | sed 's/^\.env\.//' >"$ENV_NAME_FILE"

    echo "Starting container stack with $(basename "$env_file") ..."
    compose "$env_file" up -d --build

    local bind port base
    bind="$(grep -E '^HERM_HTTP_BIND=' "$env_file" | tail -1 | cut -d= -f2- || true)"
    port="$(grep -E '^HERM_HTTP_PORT=' "$env_file" | tail -1 | cut -d= -f2- || true)"
    base="$(grep -E '^HERM_APP_BASE_PATH=' "$env_file" | tail -1 | cut -d= -f2- || true)"
    echo "Up: http://${bind:-127.0.0.1}:${port:-8080}${base%/}/ (logs: ./run.sh docker logs -f)"
}

docker_stop() {
    local env_file
    env_file="$(docker_env_file "${1:-}")" || return 1
    compose "$env_file" down
    echo "Stopped."
}

docker_status() {
    local env_file
    if ! env_file="$(docker_env_file "${1:-}" 2>/dev/null)"; then
        echo "docker: no env file selected."
        return 0
    fi
    compose "$env_file" ps
}

docker_logs() {
    local env_file
    env_file="$(docker_env_file "" )" || return 1
    if [[ "${1:-}" == "-f" ]]; then
        compose "$env_file" logs -f
    else
        compose "$env_file" logs --tail "${1:-200}"
    fi
}

docker_clean() {
    local env_file
    env_file="$(docker_env_file "${1:-}")" || return 1
    echo "Removing containers, networks and volumes..."
    compose "$env_file" down --volumes --remove-orphans
    rm -f "$ENV_NAME_FILE"
    echo "Clean."
}

# ------------------------------------------------------------------ dispatch

RUNTIME="dotnet"
case "${1:-}" in
    dotnet|docker)
        RUNTIME="$1"
        shift
        ;;
esac

COMMAND="${1:-}"
[[ $# -gt 0 ]] && shift || true

case "$RUNTIME:$COMMAND" in
    dotnet:start)   dotnet_start "${1:-$DEFAULT_PORT}" ;;
    dotnet:stop)    dotnet_stop ;;
    dotnet:restart) dotnet_stop; dotnet_start "${1:-$DEFAULT_PORT}" ;;
    dotnet:logs)    dotnet_logs "${1:-}" ;;
    dotnet:clean)   dotnet_clean ;;
    docker:start)   docker_start "${1:-}" ;;
    docker:stop)    docker_stop "${1:-}" ;;
    docker:restart) docker_stop "${1:-}"; docker_start "${1:-}" ;;
    docker:logs)    docker_logs "${1:-}" ;;
    docker:status)  docker_status "${1:-}" ;;
    docker:clean)   docker_clean "${1:-}" ;;
    dotnet:status)
        dotnet_status
        docker_status "" 2>/dev/null || true
        ;;
    *) usage 1 ;;
esac
