#!/usr/bin/env bash
# guard.sh — Loop Engineering の安全装置 (denylist / budget / timeout)
#
# Loop Design Checklist の "Escalation & Safety" / "Budget & Limits" に対応する。
# プロンプト (worker-contract) による禁止は破られうるため、ここで機械的に強制する。

# worker が編集してはいけないパスの既定 denylist (前方一致)。
# tasks.toml の [meta] denylist = ["..."] で追加できる (置換ではなく追加)。
GUARD_DEFAULT_DENYLIST=(".github/" ".ralph/" ".claude/" ".ci/" "LESSONS.md")

# guard_denylist_check <task-id> <worktree>
# worker branch の diff (main...HEAD) を denylist と照合し、
# 違反ファイルがあれば一覧を stderr に出して 1 を返す (呼び出し側が blocked にする)。
guard_denylist_check() {
  local task_id="$1" worktree="$2"
  local meta patterns=() extra=() changed f p
  meta=$(dag_meta_json)
  mapfile -t extra < <(echo "$meta" | jq -r '(.denylist // [])[]')
  patterns=("${GUARD_DEFAULT_DENYLIST[@]}")
  (( ${#extra[@]} > 0 )) && patterns+=("${extra[@]}")

  changed=$(git -C "$worktree" diff --name-only "main...HEAD" 2>/dev/null || true)
  [[ -z "$changed" ]] && return 0

  local violations=()
  while IFS= read -r f; do
    [[ -z "$f" ]] && continue
    for p in "${patterns[@]}"; do
      [[ -z "$p" ]] && continue
      if [[ "$f" == "$p" || "$f" == "$p"* ]]; then
        violations+=("$f (denylist: $p)")
        break
      fi
    done
  done <<<"$changed"

  if (( ${#violations[@]} > 0 )); then
    log_error "denylist violation in $task_id (merge を拒否):"
    printf '  %s\n' "${violations[@]}" >&2
    return 1
  fi
  return 0
}

# guard_task_timed_out <started_at-iso8601> <timeout-minutes>
# timeout-minutes が 0/空なら常に false。経過時間が上限を超えたら 0 を返す。
guard_task_timed_out() {
  local started_at="$1" timeout_min="${2:-0}"
  [[ -z "$timeout_min" || "$timeout_min" == "0" || "$timeout_min" == "null" ]] && return 1
  local start_epoch now_epoch
  start_epoch=$(date -ud "$started_at" +%s 2>/dev/null) || return 1
  now_epoch=$(date -u +%s)
  (( now_epoch - start_epoch > timeout_min * 60 ))
}

# guard_budget_exceeded <total-cost-usd> <max-cost-usd>
# max が 0/空なら無制限。float 比較は awk で行う。
guard_budget_exceeded() {
  local total="${1:-0}" max="${2:-0}"
  [[ -z "$max" || "$max" == "0" || "$max" == "null" ]] && return 1
  awk -v t="$total" -v m="$max" 'BEGIN { exit !(t >= m) }'
}
