#!/usr/bin/env bash
# Local dev runner for HERM-MAPPER-APP.
#   ./run.sh start [port]  - build and start the app in the background
#   ./run.sh stop          - stop the background app
#   ./run.sh restart       - stop, then start again
#   ./run.sh status        - show whether the app is running
#   ./run.sh logs [-f]     - print (or follow) the app log
#   ./run.sh clean         - stop the app, remove bin/obj and the run artefacts
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$ROOT_DIR/src/HERM-MAPPER-APP/HERM-MAPPER-APP.csproj"
RUN_DIR="$ROOT_DIR/.run"
PID_FILE="$RUN_DIR/app.pid"
LOG_FILE="$RUN_DIR/app.log"
PORT_FILE="$RUN_DIR/app.port"
DEFAULT_PORT="5143"

is_running() {
    [[ -f "$PID_FILE" ]] || return 1
    local pid
    pid="$(cat "$PID_FILE")"
    [[ -n "$pid" ]] && kill -0 "$pid" 2>/dev/null
}

app_url() {
    local port="$DEFAULT_PORT"
    [[ -f "$PORT_FILE" ]] && port="$(cat "$PORT_FILE")"
    echo "http://localhost:$port"
}

start_app() {
    local port="${1:-$DEFAULT_PORT}"

    if is_running; then
        echo "Already running (pid $(cat "$PID_FILE")) on $(app_url)"
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
        if ! is_running; then
            echo "Failed to start. Last log lines:" >&2
            tail -n 40 "$LOG_FILE" >&2 || true
            return 1
        fi
        sleep 1
    done

    echo "Started but no HTTP response yet. Check: ./run.sh logs" >&2
    return 1
}

stop_app() {
    if ! is_running; then
        echo "Not running."
        rm -f "$PID_FILE"
        return 0
    fi

    local pid
    pid="$(cat "$PID_FILE")"
    echo "Stopping pid $pid ..."
    pkill -TERM -P "$pid" 2>/dev/null || true
    kill -TERM "$pid" 2>/dev/null || true

    for _ in $(seq 1 20); do
        is_running || break
        sleep 0.5
    done

    if is_running; then
        pkill -KILL -P "$pid" 2>/dev/null || true
        kill -KILL "$pid" 2>/dev/null || true
    fi

    rm -f "$PID_FILE"
    echo "Stopped."
}

case "${1:-}" in
    start)
        start_app "${2:-$DEFAULT_PORT}"
        ;;
    stop)
        stop_app
        ;;
    restart)
        stop_app
        start_app "${2:-$DEFAULT_PORT}"
        ;;
    status)
        if is_running; then
            echo "Running (pid $(cat "$PID_FILE")) on $(app_url)"
        else
            echo "Not running."
        fi
        ;;
    logs)
        [[ -f "$LOG_FILE" ]] || { echo "No log yet at $LOG_FILE"; exit 0; }
        if [[ "${2:-}" == "-f" ]]; then
            tail -f "$LOG_FILE"
        else
            tail -n "${2:-200}" "$LOG_FILE"
        fi
        ;;
    clean)
        stop_app
        echo "Removing build output and run artefacts..."
        dotnet clean "$PROJECT" -c Debug --nologo -v minimal || true
        rm -rf "$ROOT_DIR/src/HERM-MAPPER-APP/bin" "$ROOT_DIR/src/HERM-MAPPER-APP/obj" "$RUN_DIR"
        echo "Clean."
        ;;
    *)
        echo "Usage: ./run.sh {start [port]|stop|restart|status|logs [-f|lines]|clean}"
        exit 1
        ;;
esac
