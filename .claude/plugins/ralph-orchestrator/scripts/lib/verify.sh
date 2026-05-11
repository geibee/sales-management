#!/usr/bin/env bash
# verify.sh — run task-specific or generic verify script in worker's worktree

# Run verify for a completed worker. Returns the exit code of the verify script.
verify_run() {
  local task_id="$1"
  local task_json
  task_json=$(dag_task "$task_id")
  local worktree
  worktree=$(state_get ".tasks[\"$task_id\"].worktree")
  local verify_script
  verify_script=$(echo "$task_json" | jq -r '.verify // ""')

  # Resolve verify path (either absolute or relative to project root)
  # Prefer project-local _generic.sh if present (allows non-MoonBit projects).
  local generic_default="$worktree/.ralph/verify/_generic.sh"
  [[ -f "$generic_default" ]] || generic_default="$PLUGIN_ROOT/scripts/verify/_generic.sh"
  if [[ -z "$verify_script" || "$verify_script" == "null" ]]; then
    verify_script="$generic_default"
  elif [[ "$verify_script" != /* ]]; then
    verify_script="$worktree/$verify_script"
    [[ -f "$verify_script" ]] || verify_script="$generic_default"
  fi

  if [[ ! -f "$verify_script" ]]; then
    log_error "verify script not found: $verify_script"
    return 127
  fi

  local baseline
  baseline=$(state_get ".tasks[\"$task_id\"].baseline_test_count // 0")
  local log_file="$LOGS_DIR/${task_id}.verify.log"

  log_info "running verify for $task_id: $verify_script"
  (
    cd "$worktree"
    TASK_ID="$task_id" \
    BASELINE_TEST_COUNT="$baseline" \
    PLUGIN_ROOT="$PLUGIN_ROOT" \
    bash "$verify_script"
  ) > "$log_file" 2>&1
  local rc=$?
  if (( rc == 0 )); then
    log_ok "verify passed: $task_id"
  else
    log_warn "verify failed: $task_id (exit $rc, see $log_file)"
  fi
  return $rc
}
