#!/usr/bin/env bash
# state.sh — atomic read/write helpers for .ralph/state.json
# Schema:
#   {
#     "version": 1,
#     "started_at": "...",
#     "halted": false,
#     "halt_reason": "",
#     "tasks": {
#       "<id>": {
#         "status": "pending|running|completed|blocked|skipped",
#         "started_at": "...",
#         "completed_at": "...",
#         "worker_pid": 0,
#         "worktree": "...",
#         "branch": "...",
#         "log_file": "...",
#         "verify_exit": null,
#         "block_reason": "",
#         "baseline_test_count": 0,
#         "iteration": 0
#       }
#     }
#   }

state_init_if_missing() {
  if [[ ! -f "$STATE_JSON" ]]; then
    cat > "$STATE_JSON" <<EOF
{
  "version": 1,
  "started_at": "$(_ts)",
  "halted": false,
  "halt_reason": "",
  "tasks": {}
}
EOF
  fi
}

state_get() {
  local jq_filter="$1"
  jq -r "$jq_filter" "$STATE_JSON"
}

state_update() {
  # Forward all args (--arg/--argjson plus filter) to jq; state.json is appended.
  local tmp="${STATE_JSON}.tmp.$$"
  jq "$@" "$STATE_JSON" > "$tmp" && mv "$tmp" "$STATE_JSON"
}

state_task_status() {
  local id="$1"
  jq -r --arg id "$id" '.tasks[$id].status // "pending"' "$STATE_JSON"
}

state_set_task_field() {
  local id="$1" field="$2" value="$3" type="${4:-string}"
  local jqv
  case "$type" in
    string) jqv="\"$value\"" ;;
    number) jqv="$value" ;;
    bool)   jqv="$value" ;;
    null)   jqv="null" ;;
    *)      jqv="\"$value\"" ;;
  esac
  state_update ".tasks[\"$id\"] = (.tasks[\"$id\"] // {}) | .tasks[\"$id\"].$field = $jqv"
}

state_mark_running() {
  local id="$1" pid="$2" worktree="$3" branch="$4" log_file="$5" baseline="$6"
  local now
  now=$(_ts)
  state_update --arg id "$id" --arg now "$now" --arg wt "$worktree" \
    --arg br "$branch" --arg lf "$log_file" \
    --argjson pid "$pid" --argjson baseline "$baseline" '
    .tasks[$id] = {
      status: "running",
      started_at: $now,
      completed_at: "",
      worker_pid: $pid,
      worktree: $wt,
      branch: $br,
      log_file: $lf,
      verify_exit: null,
      block_reason: "",
      baseline_test_count: $baseline,
      iteration: ((.tasks[$id].iteration // 0) + 1)
    }
  '
}

state_mark_completed() {
  local id="$1" verify_exit="$2"
  state_update --arg id "$id" --arg now "$(_ts)" --argjson rc "$verify_exit" '
    .tasks[$id].status = "completed"
    | .tasks[$id].completed_at = $now
    | .tasks[$id].verify_exit = $rc
    | .tasks[$id].worker_pid = 0
  '
}

state_mark_blocked() {
  local id="$1" reason="$2" verify_exit="${3:-1}"
  state_update --arg id "$id" --arg now "$(_ts)" --arg reason "$reason" --argjson rc "$verify_exit" '
    .tasks[$id].status = "blocked"
    | .tasks[$id].completed_at = $now
    | .tasks[$id].block_reason = $reason
    | .tasks[$id].verify_exit = $rc
    | .tasks[$id].worker_pid = 0
  '
}

state_mark_skipped() {
  local id="$1" reason="$2"
  state_update --arg id "$id" --arg now "$(_ts)" --arg reason "$reason" '
    .tasks[$id].status = "skipped"
    | .tasks[$id].completed_at = $now
    | .tasks[$id].block_reason = $reason
  '
}

state_set_halted() {
  local reason="$1"
  state_update --arg reason "$reason" '.halted = true | .halt_reason = $reason'
  echo "$reason" > "$HALT_FLAG"
}

state_clear_halted() {
  state_update '.halted = false | .halt_reason = ""'
  rm -f "$HALT_FLAG"
}

state_running_pids() {
  jq -r '.tasks | to_entries[] | select(.value.status == "running") | "\(.key)\t\(.value.worker_pid)"' "$STATE_JSON"
}
