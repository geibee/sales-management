#!/usr/bin/env bash
set -euo pipefail

# sales-management/ 配下から起動する RALPH ループ。
# 想定: `cd sales-management && ./harness/ralph.sh prd.md` か、
#       `cd sales-management/harness && ./ralph.sh ../prd.md`
# どちらでも cwd を sales-management に揃える。

cd "$(dirname "$0")/.."   # sales-management/

PRD="${1:-prd.md}"
MAX_ITER="${MAX_ITER:-20}"
BUDGET_USD="${BUDGET_USD:-10}"
ITER=0
COST=0

all_done() { ! grep -q '^- \[ \]' "$PRD"; }

log() { echo "[ralph] $*" >&2; }

while ! all_done; do
  ITER=$((ITER + 1))

  if [ "$ITER" -gt "$MAX_ITER" ]; then
    log "max iterations ($MAX_ITER) reached"
    exit 2
  fi

  if (( $(echo "$COST > $BUDGET_USD" | bc -l) )); then
    log "budget ($BUDGET_USD USD) exceeded"
    exit 3
  fi

  TASK=$(grep -m1 '^- \[ \]' "$PRD" | sed 's/^- \[ \] //')
  log "iter=$ITER task='$TASK'"

  SESSION_OUT=$(mktemp)
  REMAINING=$(echo "$BUDGET_USD - $COST" | bc -l)
  HARNESS_DIR=$(dirname "$0")
  log "iter=$ITER remaining=\$$REMAINING session=$SESSION_OUT"
  set +e
  set -o pipefail
  claude \
    --print \
    --no-session-persistence \
    --verbose \
    --permission-mode bypassPermissions \
    --append-system-prompt "$(cat AGENTS.md .harness/lessons.md 2>/dev/null)" \
    --max-budget-usd "$REMAINING" \
    --output-format stream-json \
    "次のタスクを実装せよ:
$TASK

手順:
1. タスクの実装方針を設計する（使うライブラリ・変更するファイル・完了条件を整理する）
2. 設計に基づいて実装する
3. タスクファイルに記載された完了条件を integration test として実装する
   - 外部サービス（Keycloak 等）が必要な場合は TestContainers または自己署名 JWT で代替する
   - Jaeger UI など目視確認が必要な条件は、エンドポイントの存在確認（HTTP 200）に置き換える
4. ZAP_ENABLED=0 ./apps/api-fsharp/ci.sh が exit 0 で終わるまで修正を繰り返す
   - **ci.sh は 5〜10 分かかる重い処理**。診断目的で何度も叩かないこと
   - **ZAP_ENABLED=0 を必ず付ける**: ralph 反復中は OWASP ZAP (DAST) を skip して時間を節約する。最終検証は ralph が全ステップ完了後に 1 回だけ走らせる
   - 出力と exit code を一度で取得する正しい打ち方:
     \`ZAP_ENABLED=0 ./apps/api-fsharp/ci.sh > /tmp/ci.out 2>&1; EXIT=\$?; tail -200 /tmp/ci.out; echo \"EXIT=\$EXIT\"\`
   - 2 回連続で同じエラーになる場合は別アプローチを検討する
5. 完了したら progress.txt に '[x] $TASK' を追記する" \
    2>&1 \
    | stdbuf -oL tee "$SESSION_OUT" \
    | stdbuf -oL python3 -u "$HARNESS_DIR/stream-progress.py"
  CLAUDE_EXIT=${PIPESTATUS[0]}
  set +o pipefail
  set -e

  THIS_COST=$(grep -E '"type":"result"' "$SESSION_OUT" | tail -1 \
    | python3 -c "import json,sys
try:
  print(json.loads(sys.stdin.read()).get('total_cost_usd', 0))
except Exception:
  print(0)" 2>/dev/null || echo "0")
  COST=$(echo "$COST + $THIS_COST" | bc -l)

  CI_LOG="/tmp/ralph-ci-iter-${ITER}.log"
  # 反復中は ZAP (OWASP DAST) を skip (数分かかる重い処理)。最終検証で 1 回走らせる
  if ZAP_ENABLED=0 ./apps/api-fsharp/ci.sh > "$CI_LOG" 2>&1; then
    sed -i "s|^- \[ \] $(echo "$TASK" | sed 's/[\/&]/\\&/g')$|- [x] $TASK|" "$PRD"
    git add -A
    git commit -m "ralph iter=$ITER: $TASK" || true
    log "iter=$ITER status=ok cost=\$$THIS_COST ci_log=$CI_LOG"
  else
    log "iter=$ITER FAILED (CI log: $CI_LOG; tail follows)"
    tail -30 "$CI_LOG" >&2 || true
    log "--- end of CI tail ---"
  fi
done

log "all PRD items done. iterations=$ITER cost=\$$COST"

# 全ステップ完了後の最終 CI: ZAP を含むフルパイプラインを 1 回だけ走らせる
log "running final full CI with ZAP enabled..."
FINAL_LOG="/tmp/ralph-ci-final.log"
if ZAP_ENABLED=1 ./apps/api-fsharp/ci.sh > "$FINAL_LOG" 2>&1; then
  log "final CI passed (log: $FINAL_LOG)"
else
  log "final CI FAILED (log: $FINAL_LOG; tail follows)"
  tail -50 "$FINAL_LOG" >&2 || true
  log "--- end of final CI tail ---"
  exit 4
fi
