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
#   VERIFY_SCOPE          auto | all | backend | frontend | repo  (default: auto)
#                         repo = スコープ不問のリポジトリ横断ゲートのみ (CI のジョブ分割用)
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
# repo 横断ゲート (gitleaks 等) は変更パスによらず常に実行する (スコープ不問)。
# CI では専用ジョブ (VERIFY_SCOPE=repo) に分離するため、backend / frontend の
# 明示スコープ指定時は重複実行しない
NEED_REPO=0

# classify_paths は scripts/lib/scope.sh に分離 (bats で単体テストするため)
# shellcheck source=lib/scope.sh
source scripts/lib/scope.sh

case "$SCOPE" in
  all)      NEED_BACKEND=1; NEED_FRONTEND=1; NEED_REPO=1 ;;
  backend)  NEED_BACKEND=1 ;;
  frontend) NEED_FRONTEND=1 ;;
  repo)     NEED_REPO=1 ;;
  auto)
    NEED_REPO=1
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
  *) fail "不明な VERIFY_SCOPE: $SCOPE (auto | all | backend | frontend | repo)" ;;
esac

if [[ "${VERIFY_DETECT_ONLY:-0}" == "1" ]]; then
  # GitHub Actions のジョブ分岐用: 判定結果だけ出力して終了する
  log "detect-only: backend=$NEED_BACKEND frontend=$NEED_FRONTEND repo=$NEED_REPO"
  if [[ -n "${GITHUB_OUTPUT:-}" ]]; then
    {
      echo "backend=$NEED_BACKEND"
      echo "frontend=$NEED_FRONTEND"
      echo "repo=$NEED_REPO"
    } >>"$GITHUB_OUTPUT"
  fi
  exit 0
fi

# ---------------------------------------------------------------- repo 共通
# スコープ不問のリポジトリ横断ゲート。ドキュメントのみの変更でも実行する。
verify_repo() {
  log "=== repo 共通 (スコープ不問) ==="

  # 秘密情報は「一度 push されたら終わり」なので、変更パスを問わず PR 前に検査する
  command -v gitleaks >/dev/null 2>&1 \
    || fail "gitleaks が見つかりません (fail-closed: 秘密情報検査なしで合格にできない)"
  gitleaks detect --source . --no-banner --redact

  # bash 資産 (verify.sh / ci.sh / ralph-orchestrator lib) の静的検査。
  # fail-closed ロジックのバグ = 全ゲートの fail-open なので、ゲート自体を検査する
  command -v shellcheck >/dev/null 2>&1 \
    || fail "shellcheck が見つかりません (fail-closed: ゲートスクリプト検査なしで合格にできない)"
  # shellcheck disable=SC2046 # git ls-files のパスは空白を含まない前提 (本リポジトリ規約)
  shellcheck --severity=warning $(git ls-files '*.sh')
  log "shellcheck: OK"

  # GitHub Actions ワークフローの静的検査 (構文 / 式 / shell script injection)
  command -v actionlint >/dev/null 2>&1 \
    || fail "actionlint が見つかりません (fail-closed: workflow 検査なしで合格にできない)"
  actionlint
  log "actionlint: OK"

  # verify.sh 自身のスコープ判定 (classify_paths) の単体テスト。
  # 「未知パス → 全検証」の fail-closed をリグレッションから守る
  command -v bats >/dev/null 2>&1 \
    || fail "bats が見つかりません (fail-closed: スコープ判定のテストなしで合格にできない)"
  bats scripts/tests
  log "bats: OK"

  # openapi.yaml の破壊的変更ゲート (oasdiff breaking)。
  # AI ループは契約を「都合よく」変えがちなので、後方互換を壊す spec 変更
  # (必須フィールド追加・enum 削除・型変更等) を PR 時点で機械検出する
  local spec="apps/api-fsharp/openapi.yaml"
  local spec_base
  spec_base=$(git merge-base HEAD "$BASE_REF" 2>/dev/null) \
    || fail "基準 ref '$BASE_REF' を解決できません (fail-closed: openapi.yaml の破壊的変更を検査できない)"

  if git diff --quiet "$spec_base" -- "$spec"; then
    log "openapi.yaml は $BASE_REF から変更なし — oasdiff スキップ (検査対象の契約変更なし)"
  else
    command -v oasdiff >/dev/null 2>&1 \
      || fail "oasdiff が見つかりません (fail-closed: 契約変更は破壊的変更検査なしで合格にできない)"

    local base_spec
    base_spec=$(mktemp --suffix=.yaml)
    git show "$spec_base:$spec" >"$base_spec"

    if oasdiff breaking --fail-on ERR "$base_spec" "$spec"; then
      log "oasdiff: 破壊的変更なし"
    else
      # 意図した破壊的変更 (実装に合わせた契約の厳格化等) は、承認ファイルに
      # 現行 openapi.yaml の blob ハッシュを記録してコミットすることで通す。
      # spec をさらに変更するとハッシュが合わなくなるため fail-closed のまま。
      # 承認の履歴は git log (このファイルの変更) で追跡できる
      local approved current_hash
      approved=$(cat apps/api-fsharp/.openapi-breaking-approved 2>/dev/null || true)
      current_hash=$(git hash-object "$spec")

      if [[ "$approved" == "$current_hash" ]]; then
        log "oasdiff: 破壊的変更を検出したが .openapi-breaking-approved と一致 (承認済み)"
      else
        rm -f "$base_spec"
        fail "openapi.yaml に破壊的変更が含まれます (oasdiff breaking)。意図した変更なら apps/api-fsharp/.openapi-breaking-approved に $current_hash を記録してレビューを受けてください"
      fi
    fi

    rm -f "$base_spec"
  fi

  log "repo PASS"
}

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

  # FSharpLint (nightly の SARIF 記録から exit code ゲートへ昇格。SARIF 出力は ci.sh のまま)
  local lint_out lint_warnings
  if ! lint_out=$(dotnet dotnet-fsharplint lint src/SalesManagement/SalesManagement.fsproj 2>&1); then
    echo "$lint_out"
    fail "FSharpLint の実行が失敗しました"
  fi
  echo "$lint_out" | grep -qE 'Summary: [0-9]+ warnings' \
    || { echo "$lint_out"; fail "FSharpLint の出力から Summary が読めません (fail-closed: 検証できないものを緑にしない)"; }
  lint_warnings=$(echo "$lint_out" | grep -oE 'Summary: [0-9]+ warnings' | grep -oE '[0-9]+' | head -1)
  if [[ "$lint_warnings" != "0" ]]; then
    echo "$lint_out"
    fail "FSharpLint warnings: ${lint_warnings} 件 (マージゲートは 0 件必須)"
  fi
  log "FSharpLint: warnings 0"

  command -v python3 >/dev/null 2>&1 \
    || fail "python3 が見つかりません (fail-closed: カバレッジラチェットに必要)"

  local out current baseline
  rm -rf coverage
  # フィルタなしの dotnet test はアーキテクチャテスト (Category=Architecture) と
  # Broker レス Pact 検証も含めて実行する = どちらもマージゲート
  out=$(dotnet test tests/SalesManagement.Tests --collect:"XPlat Code Coverage" --results-directory ./coverage 2>&1) \
    || { echo "$out"; fail "backend テストが失敗しました"; }
  echo "$out"

  # VSTest 形式 "Passed: NN" / MTP 形式 "Total tests: NN" の両方に対応
  current=$(echo "$out" | grep -oE 'Passed:[[:space:]]*[0-9]+' | grep -oE '[0-9]+' | tail -1 || echo 0)
  [[ -z "$current" || "$current" == "0" ]] \
    && current=$(echo "$out" | grep -oE 'Total tests: [0-9]+' | grep -oE '[0-9]+' | tail -1 || echo 0)
  [[ -z "$current" ]] && current=0
  baseline="${BASELINE_TEST_COUNT:-0}"
  (( current >= baseline )) || fail "backend テスト数が退行: $current < baseline $baseline"
  log "backend tests passed: $current (baseline $baseline)"

  # カバレッジラチェット (coverage-baseline.json から退行したら失敗)。
  # テスト数ラチェットは「薄いテストで数だけ稼ぐ」ゲーミングに弱いため併用する
  local cobertura
  cobertura=$(find coverage -name 'coverage.cobertura.xml' | head -1)
  [[ -n "$cobertura" ]] \
    || fail "coverage.cobertura.xml が生成されていません (fail-closed: 計測できないものを緑にしない)"
  python3 scripts/coverage-ratchet.py "$cobertura"

  # 契約カバレッジラチェット (統合テストが 2xx で到達した operationId の記録と
  # openapi.yaml の全 operation を突合し、未到達 operation の増加 = 新規 API の
  # テスト追加漏れを検出する)
  [[ -f coverage/operation-coverage.json ]] \
    || fail "operation-coverage.json が生成されていません (fail-closed: 契約カバレッジを検証できない)"
  python3 scripts/operation-coverage-ratchet.py coverage/operation-coverage.json

  log "backend PASS"

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
  # デッドコード検出 (未使用 export / 未使用依存 / 未参照ファイル。issue #9 Tier2-16)
  pnpm knip
  # テスト + カバレッジラチェット (coverage-baseline.json から退行したら失敗)
  pnpm test:coverage
  # 本番ビルド (typecheck では検出できない Vite ビルド破壊 —
  # 動的 import / asset 解決 / Tailwind 等 — を PR 時点で検出する)
  pnpm build
  # バックエンドレス smoke E2E (MSW モックでの主要導線。issue #9 Tier2-16)。
  # chromium 未導入環境では fail-closed にせずスキップすると素通りになるため、
  # playwright 実行自体に失敗解決を委ねる (ブラウザ欠如はエラーで落ちる)
  pnpm test:e2e:smoke
  log "frontend PASS"

  popd >/dev/null
}

[[ $NEED_REPO     -eq 1 ]] && verify_repo
[[ $NEED_BACKEND  -eq 1 ]] && verify_backend
[[ $NEED_FRONTEND -eq 1 ]] && verify_frontend

log "PASS: 統合 verify 完了 (repo=$NEED_REPO backend=$NEED_BACKEND frontend=$NEED_FRONTEND)"
