#!/usr/bin/env bash
# verify.sh — リポジトリ統合 verify ゲート (fail-closed)
#
# バックエンド (apps/api-fsharp) とフロントエンド (apps/frontend) の変更スコープを
# 判定し、該当スコープの build / format / lint / test を実行する。
# ralph-orchestrator のデフォルト verify (_generic.sh) から呼ばれるほか、手動でも使える:
#
#   bash scripts/verify.sh                     # 変更スコープを自動判定
#   VERIFY_SCOPE=all bash scripts/verify.sh    # 全スコープ強制
#
# 設計原則 (Loop Design Checklist / arXiv:2607.00038 の検証観点):
#   - fail-closed: 必要なツールチェーンが無い場合は「スキップして合格」ではなく失敗させる。
#     検証できなかったものを緑にしない (自動マージの前提が崩れるため)
#   - スコープ判定で分類できないパスは両スコープを要求する (未知 = 全部検証)
#   - E2E (Playwright) と重量級検査 (ZAP / Schemathesis 等) は対象外。
#     それらは apps/api-fsharp/ci.sh と `pnpm test:e2e` で別途実行する
#
# Env:
#   VERIFY_SCOPE          auto | all | backend | frontend  (default: auto)
#   VERIFY_BASE_REF       auto 判定の基準 ref               (default: main)
#   VERIFY_DETECT_ONLY    1 ならスコープ判定だけ行い結果を出力して終了 (CI のジョブ分岐用)
#   BASELINE_TEST_COUNT   バックエンドテスト pass 数の下限  (ralph orchestrator が設定)
#   TASK_ID               ログ表示用
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

SCOPE="${VERIFY_SCOPE:-auto}"
BASE_REF="${VERIFY_BASE_REF:-main}"

log() { echo "[verify] $*"; }
fail() { echo "[verify] FAIL: $*" >&2; exit 1; }

# ---------------------------------------------------------------- スコープ判定
NEED_BACKEND=0
NEED_FRONTEND=0

classify_paths() {
  # 引数: 変更ファイルパス (改行区切りを while read で受ける)
  local path
  while IFS= read -r path; do
    [[ -z "$path" ]] && continue
    case "$path" in
      apps/api-fsharp/openapi.yaml | .spectral.yaml)
        # API 契約は両スコープに影響する (frontend 側で Spectral lint / 契約テストが走る)
        NEED_BACKEND=1; NEED_FRONTEND=1 ;;
      apps/api-fsharp/* | dsl/* | pacts/*)
        NEED_BACKEND=1 ;;
      apps/frontend/*)
        NEED_FRONTEND=1 ;;
      docs/* | *.md)
        ;; # ドキュメントのみの変更は検証対象外
      *)
        # 分類できないパス (ルート設定 / .claude / scripts など) は全部検証する
        NEED_BACKEND=1; NEED_FRONTEND=1 ;;
    esac
  done
}

case "$SCOPE" in
  all)      NEED_BACKEND=1; NEED_FRONTEND=1 ;;
  backend)  NEED_BACKEND=1 ;;
  frontend) NEED_FRONTEND=1 ;;
  auto)
    if base=$(git merge-base HEAD "$BASE_REF" 2>/dev/null); then
      changed=$( { git diff --name-only "$base"; git ls-files --others --exclude-standard; } | sort -u )
      if [[ -z "$changed" ]]; then
        log "変更なし ($BASE_REF と同一) — 全スコープを検証します"
        NEED_BACKEND=1; NEED_FRONTEND=1
      else
        classify_paths <<<"$changed"
        log "変更ファイル ($(wc -l <<<"$changed") 件) から判定: backend=$NEED_BACKEND frontend=$NEED_FRONTEND"
      fi
    else
      log "基準 ref '$BASE_REF' を解決できません — 全スコープを検証します"
      NEED_BACKEND=1; NEED_FRONTEND=1
    fi
    ;;
  *) fail "不明な VERIFY_SCOPE: $SCOPE (auto | all | backend | frontend)" ;;
esac

if [[ "${VERIFY_DETECT_ONLY:-0}" == "1" ]]; then
  # GitHub Actions のジョブ分岐用: 判定結果だけ出力して終了する
  log "detect-only: backend=$NEED_BACKEND frontend=$NEED_FRONTEND"
  if [[ -n "${GITHUB_OUTPUT:-}" ]]; then
    {
      echo "backend=$NEED_BACKEND"
      echo "frontend=$NEED_FRONTEND"
    } >>"$GITHUB_OUTPUT"
  fi
  exit 0
fi

if [[ $NEED_BACKEND -eq 0 && $NEED_FRONTEND -eq 0 ]]; then
  log "PASS: 検証対象の変更なし (ドキュメントのみ)"
  exit 0
fi

# ---------------------------------------------------------------- backend
verify_backend() {
  log "=== backend (apps/api-fsharp) ==="
  command -v dotnet >/dev/null 2>&1 \
    || fail "dotnet CLI が見つかりません (fail-closed: backend 変更は dotnet なしで合格にできない)"

  pushd apps/api-fsharp >/dev/null

  # 新規 worktree / CI ランナーではローカルツール (fantomas 等) が未復元
  dotnet tool restore
  dotnet build src/SalesManagement --warnaserror
  dotnet build tests/SalesManagement.Tests --warnaserror
  dotnet fantomas --check src/ tests/

  local out current baseline
  out=$(dotnet test tests/SalesManagement.Tests 2>&1) || { echo "$out"; fail "backend テストが失敗しました"; }
  echo "$out"

  # VSTest 形式 "Passed: NN" / MTP 形式 "Total tests: NN" の両方に対応
  current=$(echo "$out" | grep -oE 'Passed:[[:space:]]*[0-9]+' | grep -oE '[0-9]+' | tail -1 || echo 0)
  [[ -z "$current" || "$current" == "0" ]] \
    && current=$(echo "$out" | grep -oE 'Total tests: [0-9]+' | grep -oE '[0-9]+' | tail -1 || echo 0)
  [[ -z "$current" ]] && current=0
  baseline="${BASELINE_TEST_COUNT:-0}"
  (( current >= baseline )) || fail "backend テスト数が退行: $current < baseline $baseline"
  log "backend PASS: tests passed: $current (baseline $baseline)"

  popd >/dev/null
}

# ---------------------------------------------------------------- frontend
verify_frontend() {
  log "=== frontend (apps/frontend) ==="
  command -v pnpm >/dev/null 2>&1 \
    || fail "pnpm が見つかりません (fail-closed: frontend 変更は pnpm なしで合格にできない)"

  pushd apps/frontend >/dev/null

  # 新規 worktree では node_modules が無いので毎回実行する (lockfile 一致なら高速)
  pnpm install --frozen-lockfile
  pnpm typecheck
  pnpm lint
  pnpm lint:contracts
  # 生成コードドリフト検査: generated.ts が openapi.yaml と同期しているか
  pnpm check:contracts-drift
  # テスト + カバレッジラチェット (coverage-baseline.json から退行したら失敗)
  pnpm test:coverage
  log "frontend PASS"

  popd >/dev/null
}

[[ $NEED_BACKEND  -eq 1 ]] && verify_backend
[[ $NEED_FRONTEND -eq 1 ]] && verify_frontend

log "PASS: 統合 verify 完了 (backend=$NEED_BACKEND frontend=$NEED_FRONTEND)"
