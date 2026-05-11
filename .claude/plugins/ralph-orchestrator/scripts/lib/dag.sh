#!/usr/bin/env bash
# dag.sh — parse tasks.toml + resolve ready tasks based on state.json

# Convert tasks.toml to JSON (cached at .ralph/tasks.cache.json)
dag_parse() {
  [[ -f "$TASKS_TOML" ]] || die "tasks.toml not found at $TASKS_TOML"
  python3 - "$TASKS_TOML" <<'PY'
import sys, json
try:
    import tomllib
except ImportError:
    try:
        import tomli as tomllib
    except ImportError:
        sys.stderr.write("ERROR: ralph-orchestrator needs Python 3.11+ (tomllib) or tomli installed\n")
        sys.exit(2)
with open(sys.argv[1], "rb") as f:
    data = tomllib.load(f)
print(json.dumps(data))
PY
}

# Get task list as JSON
dag_tasks_json() {
  dag_parse | jq '.tasks // []'
}

# Get meta config (returns JSON)
dag_meta_json() {
  dag_parse | jq '.meta // {}'
}

# Get a single task spec by id (returns JSON object or "null")
dag_task() {
  local id="$1"
  dag_parse | jq --arg id "$id" '.tasks // [] | map(select(.id == $id)) | .[0] // null'
}

# Lint: structural validation
dag_lint() {
  local data
  data=$(dag_parse)
  local errors=0

  # all task IDs unique
  local dup
  dup=$(echo "$data" | jq -r '.tasks // [] | group_by(.id) | map(select(length > 1)) | map(.[0].id) | .[]')
  if [[ -n "$dup" ]]; then
    log_error "duplicate task IDs: $dup"
    ((errors++))
  fi

  # all referenced ids exist (depends_on, parallel_with, blocks)
  local all_ids
  all_ids=$(echo "$data" | jq -r '.tasks // [] | map(.id) | join(" ")')
  for ref_field in depends_on parallel_with blocks; do
    local refs
    refs=$(echo "$data" | jq -r ".tasks // [] | .[] | (.$ref_field // []) | .[]" | sort -u)
    for ref in $refs; do
      if ! grep -wq "$ref" <<< "$all_ids"; then
        log_warn "task $ref_field references unknown task: $ref"
        ((errors++))
      fi
    done
  done

  # cycle detection (pipe JSON via stdin to avoid shell quoting issues)
  if ! echo "$data" | python3 -c '
import json, sys
data = json.load(sys.stdin)
tasks = {t["id"]: t for t in data.get("tasks", [])}
visited = {}
def dfs(node):
    if visited.get(node) == 1:
        sys.stderr.write(f"ERROR: cycle through task: {node}\n")
        sys.exit(1)
    if visited.get(node) == 2:
        return
    visited[node] = 1
    for dep in tasks.get(node, {}).get("depends_on", []):
        dfs(dep)
    visited[node] = 2
for t in tasks:
    dfs(t)
'; then
    ((errors++))
  fi

  if (( errors > 0 )); then
    return 1
  fi
  log_ok "tasks.toml: $(echo "$data" | jq '.tasks // [] | length') tasks, no structural errors"
  return 0
}

# Tasks ready to run: deps satisfied AND not skip AND not running/completed/blocked
dag_ready_tasks() {
  local tasks_json state_json
  tasks_json=$(dag_parse | jq '.tasks // []')
  state_json=$(cat "$STATE_JSON")
  jq -n --argjson tasks "$tasks_json" --argjson state "$state_json" '
    [
      $tasks[] |
      . as $t |
      ($state.tasks[$t.id].status // "pending") as $status |
      ($t.skip // false) as $skip |
      ($t.depends_on // []) as $deps |
      select($skip | not) |
      select($status == "pending") |
      select($deps | all(. as $dep | ($state.tasks[$dep].status // "pending") == "completed")) |
      $t
    ]
  '
}

# Returns count of currently running tasks
dag_running_count() {
  jq '.tasks | to_entries | map(select(.value.status == "running")) | length' "$STATE_JSON"
}

# True if any running task is serial_only
dag_running_has_serial() {
  local tasks_json state_json
  tasks_json=$(dag_parse | jq '.tasks // []')
  state_json=$(cat "$STATE_JSON")
  jq -n --argjson tasks "$tasks_json" --argjson state "$state_json" '
    [
      $tasks[] |
      . as $t |
      select(($state.tasks[$t.id].status // "pending") == "running") |
      select($t.serial_only // false) |
      $t.id
    ] | length > 0
  '
}

# Pre-mark all skip:true tasks as skipped in state (idempotent)
dag_mark_skipped() {
  local tasks_json
  tasks_json=$(dag_parse | jq '.tasks // []')
  while IFS=$'\t' read -r id reason; do
    [[ -n "$id" ]] || continue
    if [[ "$(state_task_status "$id")" == "pending" ]]; then
      state_mark_skipped "$id" "$reason"
      log_info "skipped: $id ($reason)"
    fi
  done < <(echo "$tasks_json" | jq -r '.[] | select(.skip == true) | "\(.id)\t\(.skip_reason // "marked skip in tasks.toml")"')
}

# Print human-readable DAG summary
dag_summary() {
  local data
  data=$(dag_parse)
  local total ready running completed blocked skipped
  total=$(echo "$data" | jq '.tasks // [] | length')
  if [[ -f "$STATE_JSON" ]]; then
    ready=$(dag_ready_tasks | jq 'length')
    running=$(jq '[.tasks[] | select(.status == "running")] | length' "$STATE_JSON")
    completed=$(jq '[.tasks[] | select(.status == "completed")] | length' "$STATE_JSON")
    blocked=$(jq '[.tasks[] | select(.status == "blocked")] | length' "$STATE_JSON")
    skipped=$(jq '[.tasks[] | select(.status == "skipped")] | length' "$STATE_JSON")
  else
    ready=0; running=0; completed=0; blocked=0; skipped=0
  fi
  printf "${C_BLD}DAG status${C_RST}\n"
  printf "  total:     %d\n" "$total"
  printf "  ready:     %d\n" "$ready"
  printf "  running:   %d\n" "$running"
  printf "  completed: %d\n" "$completed"
  printf "  blocked:   %d\n" "$blocked"
  printf "  skipped:   %d\n" "$skipped"
}
