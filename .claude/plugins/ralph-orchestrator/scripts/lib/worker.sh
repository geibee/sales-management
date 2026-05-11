#!/usr/bin/env bash
# worker.sh — spawn a `claude -p` subprocess in an isolated worktree

# Render the prompt template by substituting {{ key }} placeholders.
# Usage: worker_render_prompt <task-json> > out.md
worker_render_prompt() {
  local task_json="$1"
  local tmpl="$PROJECT_ROOT/.ralph/task-prompt.tmpl.md"
  [[ -f "$tmpl" ]] || tmpl="$PLUGIN_ROOT/prompts/task-prompt.tmpl.md"
  local id title phase size files_list verify prompt_extra baseline
  id=$(echo "$task_json" | jq -r '.id')
  title=$(echo "$task_json" | jq -r '.title // .id')
  phase=$(echo "$task_json" | jq -r '.phase // ""')
  size=$(echo "$task_json" | jq -r '.size // ""')
  files_list=$(echo "$task_json" | jq -r '(.files // []) | map("- " + .) | join("\n")')
  verify=$(echo "$task_json" | jq -r '.verify // ".ralph/verify/_generic.sh"')
  prompt_extra=$(echo "$task_json" | jq -r '.prompt_extra // ""')
  baseline=$(state_get ".tasks[\"$id\"].baseline_test_count // 0")

  awk -v id="$id" -v title="$title" -v phase="$phase" -v size="$size" \
      -v files="$files_list" -v verify="$verify" -v extra="$prompt_extra" \
      -v baseline="$baseline" '
    {
      gsub(/\{\{ *id *\}\}/, id)
      gsub(/\{\{ *title *\}\}/, title)
      gsub(/\{\{ *phase *\}\}/, phase)
      gsub(/\{\{ *size *\}\}/, size)
      gsub(/\{\{ *files *\}\}/, files)
      gsub(/\{\{ *verify *\}\}/, verify)
      gsub(/\{\{ *prompt_extra *\}\}/, extra)
      gsub(/\{\{ *baseline_test_count *\}\}/, baseline)
      print
    }
  ' "$tmpl"
}

# Capture current test count to record as baseline. Returns integer.
# If <repo>/.ralph/capture-baseline.sh exists, run it instead of the default
# moon-based capture (allows non-MoonBit projects to override).
worker_capture_baseline() {
  local override="$PROJECT_ROOT/.ralph/capture-baseline.sh"
  if [[ -f "$override" ]]; then
    local count
    count=$(cd "$PROJECT_ROOT" && bash "$override" 2>/dev/null || echo 0)
    [[ -z "$count" ]] && count=0
    echo "$count"
    return
  fi
  local count
  count=$(cd "$PROJECT_ROOT" && moon test 2>&1 | grep -oE 'Total tests: [0-9]+' | grep -oE '[0-9]+' | tail -1 || echo 0)
  [[ -z "$count" ]] && count=0
  # fallback: grep the trailing summary "passed: NN, failed: NN"
  if [[ "$count" == "0" ]]; then
    count=$(cd "$PROJECT_ROOT" && moon test 2>&1 | grep -oE 'passed: [0-9]+' | grep -oE '[0-9]+' | tail -1 || echo 0)
    [[ -z "$count" ]] && count=0
  fi
  echo "$count"
}

# Spawn a worker for a single task. Returns 0 on successful spawn (PID written
# to state). Returns nonzero if worktree creation or spawn failed.
worker_spawn() {
  local task_json="$1"
  local id
  id=$(echo "$task_json" | jq -r '.id')

  local meta
  meta=$(dag_meta_json)
  local worktree_prefix branch_prefix model
  worktree_prefix=$(echo "$meta" | jq -r '.worktree_prefix // "../mr-ralph-"')
  branch_prefix=$(echo "$meta" | jq -r '.branch_prefix // "ralph/"')
  model=$(echo "$meta" | jq -r '.default_model // "opus"')

  local worktree="${worktree_prefix}${id}"
  local branch="${branch_prefix}${id}"
  local log_file="$LOGS_DIR/${id}.log"
  local prompt_file="$LOGS_DIR/${id}.prompt.md"
  local stream_file="$LOGS_DIR/${id}.stream.jsonl"

  # capture baseline test count BEFORE worker starts (in main worktree)
  local baseline
  baseline=$(worker_capture_baseline)

  # create worktree (idempotent: remove first if exists)
  if git -C "$PROJECT_ROOT" worktree list | grep -q " $worktree "; then
    log_warn "worktree $worktree already exists, removing"
    git -C "$PROJECT_ROOT" worktree remove --force "$worktree" 2>/dev/null || true
  fi
  if git -C "$PROJECT_ROOT" branch --list "$branch" | grep -q .; then
    git -C "$PROJECT_ROOT" branch -D "$branch" 2>/dev/null || true
  fi

  if ! git -C "$PROJECT_ROOT" worktree add "$worktree" -b "$branch" main 2>"$log_file"; then
    log_error "failed to create worktree for $id (see $log_file)"
    return 1
  fi

  # render prompt: worker contract + rendered task spec.
  # Concatenating these into --append-system-prompt removes the dependency
  # on the spawned process discovering the plugin's agent definition.
  {
    cat "$PLUGIN_ROOT/prompts/worker-contract.md"
    echo ""
    echo "---"
    echo ""
    worker_render_prompt "$task_json"
  } > "$prompt_file"

  # build allowed-tools list
  local allowed_tools='Bash Edit Write Read Grep Glob Skill TaskCreate TaskUpdate TaskList'

  # find settings file in project (optional)
  local settings_arg=()
  if [[ -f "$worktree/.claude/settings.local.json" ]]; then
    settings_arg=(--settings "$worktree/.claude/settings.local.json")
  fi

  # Spawn detached. Use setsid so child survives orchestrator restart.
  # stream-json output requires --verbose. claude -p needs an initial user
  # prompt; the task spec is in --append-system-prompt, this is just a kickoff.
  local kickoff="Begin executing the RALPH task described in your system prompt. Follow the contract strictly. End with <task-status>done</task-status> on success or <task-status>blocked: ...</task-status> on failure."
  log_info "spawning worker for $id (worktree=$worktree branch=$branch)"
  (
    cd "$worktree"
    setsid claude -p "$kickoff" \
      --model "$model" \
      --append-system-prompt "$(cat "$prompt_file")" \
      --allowed-tools "$allowed_tools" \
      --output-format stream-json \
      --verbose \
      "${settings_arg[@]}" \
      < /dev/null > "$stream_file" 2>>"$log_file" &
    echo $!
  ) > "$LOGS_DIR/${id}.pid" 2>/dev/null
  local pid
  pid=$(cat "$LOGS_DIR/${id}.pid" 2>/dev/null || echo 0)
  if [[ -z "$pid" || "$pid" == "0" ]]; then
    log_error "failed to spawn claude for $id"
    return 1
  fi

  state_mark_running "$id" "$pid" "$worktree" "$branch" "$stream_file" "$baseline"
  log_ok "spawned $id pid=$pid"
}

# Check if a worker pid is still alive
worker_alive() {
  local pid="$1"
  [[ -n "$pid" && "$pid" != "0" ]] && kill -0 "$pid" 2>/dev/null
}

# Extract <task-status>...</task-status> from worker stream-json log.
# Returns "done" / "blocked: <reason>" / "" (if not yet emitted).
worker_extract_status() {
  local stream_file="$1"
  [[ -f "$stream_file" ]] || { echo ""; return; }
  # The stream emits assistant messages as JSON lines; concatenate text content
  # and search for <task-status> tag.
  local all_text
  all_text=$(jq -rs '
    map(select(.type == "assistant" or .type == "stream_event"))
    | map(.message.content[]? // .delta.text? // empty | select(type == "object" and .type == "text") | .text)
    | flatten
    | join("\n")
  ' "$stream_file" 2>/dev/null || echo "")
  # fallback: grep raw
  if [[ -z "$all_text" ]]; then
    all_text=$(cat "$stream_file" 2>/dev/null)
  fi
  # extract first <task-status>...</task-status>
  echo "$all_text" | perl -0777 -ne '
    if (/<task-status>(.*?)<\/task-status>/s) { my $s = $1; $s =~ s/^\s+|\s+$//g; print $s; exit }
  '
}
