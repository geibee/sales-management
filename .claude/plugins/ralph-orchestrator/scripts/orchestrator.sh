#!/usr/bin/env bash
# orchestrator.sh — entry point for /ralph-orch slash command and daemon loop
# Subcommands: start, run <id>, status, resume, dry-run, stop, logs <id>, lint, help

set -euo pipefail

PLUGIN_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PLUGIN_ROOT

# shellcheck source=lib/common.sh
source "$PLUGIN_ROOT/scripts/lib/common.sh"
# shellcheck source=lib/state.sh
source "$PLUGIN_ROOT/scripts/lib/state.sh"
# shellcheck source=lib/dag.sh
source "$PLUGIN_ROOT/scripts/lib/dag.sh"
# shellcheck source=lib/worker.sh
source "$PLUGIN_ROOT/scripts/lib/worker.sh"
# shellcheck source=lib/verify.sh
source "$PLUGIN_ROOT/scripts/lib/verify.sh"
# shellcheck source=lib/merge.sh
source "$PLUGIN_ROOT/scripts/lib/merge.sh"

# Default tick interval for daemon polling (seconds)
TICK_INTERVAL="${RALPH_TICK_INTERVAL:-10}"

cmd_help() {
  cat <<EOF
${C_BLD}ralph-orchestrator${C_RST} — DAG-based multi-session RALPH

Subcommands:
  start                  Start orchestrator daemon (background). Clears halt marker. For interactive use.
  tick                   Cron-friendly idempotent wake-up. Respects halt; skips if no ready work.
  run <task-id>          Run a single task synchronously (debug).
  status                 Show DAG and per-task status.
  resume                 Clear halt marker and re-trigger start.
  dry-run                Print next batch of tasks; spawn nothing.
  stop                   SIGTERM running workers; preserve worktrees.
  logs <task-id>         Tail child Claude's stream-json log.
  lint                   Validate tasks.toml structure.
  help                   This message.

Project must contain .ralph/tasks.toml.
EOF
}

cmd_lint() {
  require_project_root
  require_tools
  dag_lint
}

cmd_status() {
  require_project_root
  require_tools
  state_init_if_missing
  dag_summary
  echo ""
  if daemon_running; then
    printf "${C_GRN}daemon: running (pid %s)${C_RST}\n" "$(cat "$PIDFILE")"
  else
    printf "${C_DIM}daemon: not running${C_RST}\n"
  fi
  if [[ -f "$HALT_FLAG" ]]; then
    printf "${C_YEL}halted: $(cat "$HALT_FLAG")${C_RST}\n"
  fi
  echo ""
  echo "Per-task status:"
  printf "  %-12s %-12s %-10s %s\n" "ID" "STATUS" "PID" "REASON"
  jq -r '
    .tasks | to_entries[] |
    "\(.key)\t\(.value.status // "-")\t\(.value.worker_pid // 0)\t\(.value.block_reason // "")"
  ' "$STATE_JSON" | while IFS=$'\t' read -r id status pid reason; do
    printf "  %-12s %-12s %-10s %s\n" "$id" "$status" "$pid" "$reason"
  done
}

cmd_dry_run() {
  require_project_root
  require_tools
  state_init_if_missing
  dag_lint || die "tasks.toml has structural errors; aborting dry-run"
  dag_mark_skipped
  local meta pool_size
  meta=$(dag_meta_json)
  pool_size=$(echo "$meta" | jq -r '.worker_pool_size // 3')
  echo ""
  echo "${C_BLD}Next batch (up to $pool_size workers):${C_RST}"
  local ready
  ready=$(dag_ready_tasks)
  local count
  count=$(echo "$ready" | jq 'length')
  if (( count == 0 )); then
    echo "  (none ready — DAG done, blocked, or all running)"
  else
    echo "$ready" | jq -r '.[] | "  \(.id)\t[\(.phase // "-")] \(.title // "") (size=\(.size // "-"), serial=\(.serial_only // false), halt_before=\(.halt_before // false))"'
    local first_halt
    first_halt=$(echo "$ready" | jq -r 'first(.[] | select(.halt_before == true)) | .id // empty')
    if [[ -n "$first_halt" ]]; then
      echo ""
      printf "${C_YEL}halt_before${C_RST}: orchestrator will stop before %s\n" "$first_halt"
    fi
  fi
  echo ""
  dag_summary
}

cmd_run_one() {
  require_project_root
  require_tools
  state_init_if_missing
  local id="${1:-}"
  [[ -n "$id" ]] || die "usage: ralph-orch run <task-id>"
  local task_json
  task_json=$(dag_task "$id")
  [[ "$task_json" != "null" ]] || die "task not found: $id"
  log_info "running task $id (foreground)"
  with_state_lock worker_spawn "$task_json" || die "failed to spawn $id"
  local pid stream_file
  pid=$(state_get ".tasks[\"$id\"].worker_pid")
  stream_file=$(state_get ".tasks[\"$id\"].log_file")
  log_info "waiting for worker pid=$pid"
  # poll until exit
  while worker_alive "$pid"; do sleep 5; done
  log_info "worker $id exited; checking task-status marker"
  local status
  status=$(worker_extract_status "$stream_file")
  log_info "task-status: ${status:-(none)}"
  if [[ "$status" == "done" ]]; then
    if verify_run "$id"; then
      with_state_lock state_mark_completed "$id" 0
      with_state_lock merge_run "$id" || with_state_lock state_mark_blocked "$id" "merge failed" 1
    else
      with_state_lock state_mark_blocked "$id" "verify failed" $?
    fi
  else
    with_state_lock state_mark_blocked "$id" "${status:-no done marker}" 1
  fi
  cmd_status
}

cmd_start() {
  require_project_root
  require_tools
  state_init_if_missing
  dag_lint || die "tasks.toml has structural errors; aborting start"
  dag_mark_skipped

  if daemon_running; then
    log_info "orchestrator already running (pid $(cat "$PIDFILE"))"
    return 0
  fi

  rm -f "$STOP_FLAG" "$HALT_FLAG"
  state_clear_halted

  log_info "starting orchestrator daemon"
  # detach via setsid + nohup
  setsid nohup bash -c '
    cd "'"$PROJECT_ROOT"'"
    exec bash "'"$PLUGIN_ROOT"'/scripts/orchestrator.sh" _daemon
  ' >> "$LOGS_DIR/orchestrator.log" 2>&1 < /dev/null &
  local daemon_pid=$!
  disown $daemon_pid 2>/dev/null || true
  echo $daemon_pid > "$PIDFILE"
  log_ok "orchestrator started (pid $daemon_pid). Tail: $LOGS_DIR/orchestrator.log"
  echo ""
  echo "Use ${C_BLD}/ralph-orch status${C_RST} to monitor."
}

cmd_stop() {
  require_project_root
  state_init_if_missing
  if ! daemon_running; then
    log_info "orchestrator not running"
    return 0
  fi
  touch "$STOP_FLAG"
  log_info "requested daemon stop (SIGTERM)"
  local pid
  pid=$(cat "$PIDFILE")
  kill -TERM "$pid" 2>/dev/null || true
  # Also TERM running workers
  while IFS=$'\t' read -r id wpid; do
    [[ -z "$wpid" || "$wpid" == "0" ]] && continue
    log_info "TERM worker $id pid=$wpid"
    kill -TERM "$wpid" 2>/dev/null || true
  done < <(state_running_pids)
  rm -f "$PIDFILE"
}

cmd_resume() {
  require_project_root
  state_init_if_missing
  state_clear_halted
  log_info "halt marker cleared"
  cmd_start
}

# Cron-friendly idempotent wake-up. Honors halt and stop flags.
# Exit codes:
#   0 — daemon was started, already running, or correctly skipped (no work / halted)
#   non-zero only on hard errors (bad tasks.toml, missing tools)
cmd_tick() {
  require_project_root
  require_tools
  state_init_if_missing
  if [[ -f "$STOP_FLAG" ]]; then
    log_info "tick: stop flag present, skipping"
    return 0
  fi
  if [[ -f "$HALT_FLAG" ]]; then
    log_info "tick: halted ($(cat "$HALT_FLAG")) — manual /ralph-orch resume required"
    return 0
  fi
  if daemon_running; then
    log_info "tick: daemon already running (pid $(cat "$PIDFILE"))"
    return 0
  fi
  # Nothing-to-do check: skip spawning a daemon if no tasks ready
  dag_mark_skipped >/dev/null 2>&1 || true
  local ready_count
  ready_count=$(dag_ready_tasks 2>/dev/null | jq 'length' 2>/dev/null || echo 0)
  if [[ "$ready_count" == "0" ]]; then
    log_info "tick: no ready tasks, skipping"
    return 0
  fi
  log_info "tick: $ready_count task(s) ready, starting daemon"
  cmd_start
}

cmd_logs() {
  require_project_root
  state_init_if_missing
  local id="${1:-}"
  [[ -n "$id" ]] || die "usage: ralph-orch logs <task-id>"
  local stream_file="$LOGS_DIR/${id}.stream.jsonl"
  local plain_log="$LOGS_DIR/${id}.log"
  echo "=== ${stream_file}"
  if [[ -f "$stream_file" ]]; then
    # extract human-readable text from stream
    jq -r 'select(.type == "assistant") | .message.content[]? | select(.type == "text") | .text' "$stream_file" 2>/dev/null \
      || cat "$stream_file"
  else
    echo "(no stream)"
  fi
  echo "=== ${plain_log}"
  [[ -f "$plain_log" ]] && cat "$plain_log" || echo "(no log)"
}

# ============================================================================
# Daemon main loop (invoked via _daemon subcommand from cmd_start)
# ============================================================================

daemon_main() {
  require_project_root
  require_tools
  state_init_if_missing
  log_info "daemon main loop started (pid=$$, tick=${TICK_INTERVAL}s)"
  trap 'log_info "daemon received SIGTERM, exiting"; exit 0' TERM INT

  local meta pool_size
  meta=$(dag_meta_json)
  pool_size=$(echo "$meta" | jq -r '.worker_pool_size // 3')

  while true; do
    [[ -f "$STOP_FLAG" ]] && { log_info "stop flag detected, exiting"; rm -f "$STOP_FLAG" "$PIDFILE"; exit 0; }

    # 1) reap completed workers
    while IFS=$'\t' read -r id pid; do
      [[ -z "$id" ]] && continue
      if ! worker_alive "$pid"; then
        local stream_file
        stream_file=$(state_get ".tasks[\"$id\"].log_file")
        local status_marker
        status_marker=$(worker_extract_status "$stream_file")
        log_info "worker $id (pid=$pid) exited; status-marker='${status_marker:-none}'"
        if [[ "$status_marker" == "done" ]]; then
          if with_state_lock verify_run "$id"; then
            with_state_lock state_mark_completed "$id" 0
            if ! with_state_lock merge_run "$id"; then
              with_state_lock state_mark_blocked "$id" "merge failed" 1
            fi
          else
            with_state_lock state_mark_blocked "$id" "verify failed" 1
          fi
        else
          with_state_lock state_mark_blocked "$id" "${status_marker:-no done marker}" 1
        fi
      fi
    done < <(state_running_pids)

    # 2) check halt condition: next ready task with halt_before
    local ready next_halt
    ready=$(dag_ready_tasks)
    next_halt=$(echo "$ready" | jq -r 'first(.[] | select(.halt_before == true)) | .id // empty')
    if [[ -n "$next_halt" ]]; then
      local running_count
      running_count=$(dag_running_count)
      if (( running_count == 0 )); then
        log_info "halt_before reached: $next_halt — draining done, halting"
        with_state_lock state_set_halted "halt_before $next_halt"
        rm -f "$PIDFILE"
        exit 0
      else
        log_info "halt_before $next_halt pending; waiting for $running_count running task(s) to drain"
      fi
    fi

    # 3) spawn new workers up to pool_size
    local running_count
    running_count=$(dag_running_count)
    local serial_active
    serial_active=$(dag_running_has_serial)
    if [[ "$serial_active" != "true" ]]; then
      while (( running_count < pool_size )); do
        ready=$(dag_ready_tasks)
        # filter out halt_before tasks
        local next
        next=$(echo "$ready" | jq -c 'first(.[] | select((.halt_before // false) == false)) // null')
        [[ "$next" == "null" || -z "$next" ]] && break

        # serial_only: spawn alone, only if pool empty
        local is_serial
        is_serial=$(echo "$next" | jq -r '.serial_only // false')
        if [[ "$is_serial" == "true" ]] && (( running_count > 0 )); then
          # wait for current pool to drain
          break
        fi

        with_state_lock worker_spawn "$next" || {
          local id
          id=$(echo "$next" | jq -r '.id')
          log_error "spawn failed for $id"
          with_state_lock state_mark_blocked "$id" "spawn failed" 1
        }
        running_count=$(dag_running_count)

        if [[ "$is_serial" == "true" ]]; then
          # serial task: don't spawn anything alongside
          break
        fi
      done
    fi

    # 4) check end conditions: nothing running and no ready left → done
    running_count=$(dag_running_count)
    if (( running_count == 0 )); then
      ready=$(dag_ready_tasks)
      local ready_count
      ready_count=$(echo "$ready" | jq 'length')
      if (( ready_count == 0 )); then
        log_ok "daemon: no work remaining, exiting"
        rm -f "$PIDFILE"
        exit 0
      fi
    fi

    sleep "$TICK_INTERVAL"
  done
}

# ============================================================================
# Subcommand dispatch
# ============================================================================

main() {
  local sub="${1:-status}"
  shift || true
  case "$sub" in
    start)    cmd_start "$@" ;;
    tick)     cmd_tick "$@" ;;
    run)      cmd_run_one "$@" ;;
    status)   cmd_status "$@" ;;
    resume)   cmd_resume "$@" ;;
    dry-run)  cmd_dry_run "$@" ;;
    stop)     cmd_stop "$@" ;;
    logs)     cmd_logs "$@" ;;
    lint)     cmd_lint "$@" ;;
    help|-h|--help) cmd_help ;;
    _daemon)  daemon_main "$@" ;;
    *)        log_error "unknown subcommand: $sub"; cmd_help; exit 1 ;;
  esac
}

main "$@"
