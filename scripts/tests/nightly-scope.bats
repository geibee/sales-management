#!/usr/bin/env bats
# nightly の重量ジョブを PR の変更パスから選ぶ純粋関数のテーブルテスト。

setup() {
  # shellcheck source=../lib/nightly-scope.sh
  source "$BATS_TEST_DIRNAME/../lib/nightly-scope.sh"
  NIGHTLY_WORKFLOW="$BATS_TEST_DIRNAME/../../.github/workflows/nightly.yml"
  NEED_NIGHTLY_HEAVY=0
  NEED_NIGHTLY_E2E=0
}

assert_nightly_scope() {
  [ "$NEED_NIGHTLY_HEAVY" -eq "$1" ] || {
    echo "NEED_NIGHTLY_HEAVY=$NEED_NIGHTLY_HEAVY expected $1"
    return 1
  }
  [ "$NEED_NIGHTLY_E2E" -eq "$2" ] || {
    echo "NEED_NIGHTLY_E2E=$NEED_NIGHTLY_E2E expected $2"
    return 1
  }
}

@test "nightly workflow は heavy と E2E の両方" {
  classify_nightly_paths <<<".github/workflows/nightly.yml"
  assert_nightly_scope 1 1
}

@test "ci.sh は heavy のみ" {
  classify_nightly_paths <<<"apps/api-fsharp/ci.sh"
  assert_nightly_scope 1 0
}

@test "ZAP と Schemathesis の設定・変換は heavy" {
  classify_nightly_paths <<'EOF'
apps/api-fsharp/zap-rules.tsv
apps/api-fsharp/schemathesis-hooks.py
apps/api-fsharp/scripts/zap-to-sarif.py
apps/api-fsharp/scripts/junit-to-sarif.py
apps/api-fsharp/scripts/sarif-merge.py
EOF
  assert_nightly_scope 1 0
}

@test "OpenAPI と WireMock 設定は heavy" {
  classify_nightly_paths <<'EOF'
apps/api-fsharp/openapi.yaml
apps/api-fsharp/docker-compose.yml
apps/api-fsharp/wiremock/mappings/price-check-ok.json
EOF
  assert_nightly_scope 1 0
}

@test "Playwright 設定と frontend 依存定義は E2E" {
  classify_nightly_paths <<'EOF'
apps/frontend/playwright.config.ts
apps/frontend/package.json
apps/frontend/pnpm-lock.yaml
EOF
  assert_nightly_scope 0 1
}

@test "backend E2E テストは E2E" {
  classify_nightly_paths <<<"apps/frontend/tests/e2e/backend-lifecycle.spec.ts"
  assert_nightly_scope 0 1
}

@test "通常の実装やドキュメントは nightly 対象外" {
  classify_nightly_paths <<'EOF'
apps/api-fsharp/src/SalesManagement/Domain/Types.fs
apps/frontend/src/main.tsx
docs/note.md
EOF
  assert_nightly_scope 0 0
}

@test "複数パスの heavy と E2E を合成する" {
  classify_nightly_paths <<'EOF'
apps/api-fsharp/ci.sh
apps/frontend/tests/e2e/backend-case-flows.spec.ts
EOF
  assert_nightly_scope 1 1
}

@test "nightly workflow は全 PR で scope 判定する" {
  run grep -F "  pull_request:" "$NIGHTLY_WORKFLOW"
  [ "$status" -eq 0 ]
  [[ $(<"$NIGHTLY_WORKFLOW") == *'source scripts/lib/nightly-scope.sh'* ]]
  [[ $(<"$NIGHTLY_WORKFLOW") == *'classify_nightly_paths'* ]]
}

@test "heavy と E2E は scope 出力で起動する" {
  workflow=$(<"$NIGHTLY_WORKFLOW")
  [[ "$workflow" == *"if: needs.scope.outputs.heavy == '1'"* ]]
  [[ "$workflow" == *"if: needs.scope.outputs.e2e == '1'"* ]]
}

@test "nightly 結果集約は常時実行する" {
  workflow=$(<"$NIGHTLY_WORKFLOW")
  [[ "$workflow" == *'name: nightly 結果集約'* ]]
  [[ "$workflow" == *'needs: [scope, heavy, e2e]'* ]]
  [[ "$workflow" == *'if: always()'* ]]
}
