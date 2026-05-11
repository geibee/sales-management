#!/usr/bin/env bash
# common.sh — shared logging / locking / pidfile helpers for ralph-orchestrator

# colors
if [[ -t 1 ]]; then
  C_DIM=$'\033[2m'; C_RED=$'\033[31m'; C_YEL=$'\033[33m'
  C_GRN=$'\033[32m'; C_CYA=$'\033[36m'; C_BLD=$'\033[1m'; C_RST=$'\033[0m'
else
  C_DIM=""; C_RED=""; C_YEL=""; C_GRN=""; C_CYA=""; C_BLD=""; C_RST=""
fi

_ts() { date -u +'%Y-%m-%dT%H:%M:%SZ'; }

log_info()  { printf "%s%s%s %sINFO%s  %s\n"  "$C_DIM" "$(_ts)" "$C_RST" "$C_CYA" "$C_RST" "$*" >&2; }
log_warn()  { printf "%s%s%s %sWARN%s  %s\n"  "$C_DIM" "$(_ts)" "$C_RST" "$C_YEL" "$C_RST" "$*" >&2; }
log_error() { printf "%s%s%s %sERROR%s %s\n"  "$C_DIM" "$(_ts)" "$C_RST" "$C_RED" "$C_RST" "$*" >&2; }
log_ok()    { printf "%s%s%s %sOK%s    %s\n"  "$C_DIM" "$(_ts)" "$C_RST" "$C_GRN" "$C_RST" "$*" >&2; }

die() { log_error "$*"; exit 1; }

# project root detection — caller cd's to project root
require_project_root() {
  if ! git rev-parse --show-toplevel >/dev/null 2>&1; then
    die "not inside a git repository"
  fi
  PROJECT_ROOT="$(git rev-parse --show-toplevel)"
  RALPH_DIR="$PROJECT_ROOT/.ralph"
  TASKS_TOML="$RALPH_DIR/tasks.toml"
  STATE_JSON="$RALPH_DIR/state.json"
  LOGS_DIR="$RALPH_DIR/logs"
  PIDFILE="$RALPH_DIR/orchestrator.pid"
  HALT_FLAG="$RALPH_DIR/halted"
  STOP_FLAG="$RALPH_DIR/stop-requested"
  mkdir -p "$LOGS_DIR"
  export PROJECT_ROOT RALPH_DIR TASKS_TOML STATE_JSON LOGS_DIR PIDFILE HALT_FLAG STOP_FLAG
}

# Pidfile helpers (orchestrator daemon)
daemon_running() {
  [[ -f "$PIDFILE" ]] || return 1
  local pid
  pid=$(cat "$PIDFILE" 2>/dev/null) || return 1
  [[ -n "$pid" ]] || return 1
  kill -0 "$pid" 2>/dev/null
}

write_pidfile() { echo $$ > "$PIDFILE"; }
clear_pidfile() { rm -f "$PIDFILE"; }

# State lock — flock-based to keep concurrent state.json updates safe
with_state_lock() {
  local lockfile="$RALPH_DIR/state.lock"
  exec 9>"$lockfile"
  flock -x 9
  "$@"
  local rc=$?
  flock -u 9
  exec 9>&-
  return $rc
}

# tools we depend on
require_tools() {
  local missing=()
  for t in jq git python3 claude; do
    command -v "$t" >/dev/null 2>&1 || missing+=("$t")
  done
  if (( ${#missing[@]} > 0 )); then
    die "missing required tools: ${missing[*]}"
  fi
}
