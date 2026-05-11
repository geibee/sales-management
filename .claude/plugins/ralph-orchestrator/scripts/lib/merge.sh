#!/usr/bin/env bash
# merge.sh — rebase worker branch onto main, fast-forward merge, push, cleanup

# Merge a verified worker branch back into main. Returns 0 on success.
merge_run() {
  local task_id="$1"
  local meta auto_merge auto_push
  meta=$(dag_meta_json)
  auto_merge=$(echo "$meta" | jq -r '.auto_merge // true')
  auto_push=$(echo "$meta" | jq -r '.auto_push // true')

  if [[ "$auto_merge" != "true" ]]; then
    log_info "auto_merge=false, skipping merge for $task_id (worktree preserved)"
    return 0
  fi

  local worktree branch
  worktree=$(state_get ".tasks[\"$task_id\"].worktree")
  branch=$(state_get ".tasks[\"$task_id\"].branch")
  local merge_log="$LOGS_DIR/${task_id}.merge.log"

  log_info "merging $task_id ($branch) -> main"

  # Step 1: rebase worktree branch onto latest main
  if ! git -C "$worktree" fetch origin main 2>>"$merge_log"; then
    log_warn "fetch origin failed in worktree; continuing with local main"
  fi
  if ! git -C "$worktree" rebase main 2>>"$merge_log"; then
    log_error "rebase failed for $task_id (see $merge_log). worktree preserved."
    git -C "$worktree" rebase --abort 2>/dev/null || true
    return 1
  fi

  # Step 2: in main worktree, fast-forward merge the branch
  if ! git -C "$PROJECT_ROOT" merge --ff-only "$branch" 2>>"$merge_log"; then
    log_error "ff-merge failed for $task_id (see $merge_log)"
    return 1
  fi

  # Step 3: push if configured
  if [[ "$auto_push" == "true" ]]; then
    if ! git -C "$PROJECT_ROOT" push origin main 2>>"$merge_log"; then
      log_warn "push failed for $task_id (commit applied locally, see $merge_log)"
    else
      log_ok "pushed $task_id to origin/main"
    fi
  fi

  # Step 4: cleanup worktree + branch
  git -C "$PROJECT_ROOT" worktree remove --force "$worktree" 2>>"$merge_log" || true
  git -C "$PROJECT_ROOT" branch -D "$branch" 2>>"$merge_log" || true

  log_ok "merged $task_id"
  return 0
}
