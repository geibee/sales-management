#!/usr/bin/env bats
# classify_paths (scripts/lib/scope.sh) のテーブルテスト。
#
# verify のスコープ判定は「検証されるべき変更が検証されずに緑になる」
# fail-open 事故の入り口なので、分類規則を 1 ケースずつ固定する。
# 特に「未知パス → 全検証」の fail-closed が要 (issue #9 Tier1-9)。

setup() {
  # shellcheck source=../lib/scope.sh
  source "$BATS_TEST_DIRNAME/../lib/scope.sh"
  NEED_BACKEND=0
  NEED_FRONTEND=0
}

assert_scope() {
  [ "$NEED_BACKEND" -eq "$1" ] || { echo "NEED_BACKEND=$NEED_BACKEND expected $1"; return 1; }
  [ "$NEED_FRONTEND" -eq "$2" ] || { echo "NEED_FRONTEND=$NEED_FRONTEND expected $2"; return 1; }
}

@test "openapi.yaml は両スコープ (契約は backend / frontend 双方に影響)" {
  classify_paths <<<"apps/api-fsharp/openapi.yaml"
  assert_scope 1 1
}

@test ".spectral.yaml は両スコープ" {
  classify_paths <<<".spectral.yaml"
  assert_scope 1 1
}

@test "backend ソースは backend のみ" {
  classify_paths <<<"apps/api-fsharp/src/SalesManagement/Domain/Types.fs"
  assert_scope 1 0
}

@test "dsl/ は backend (DSL は backend 実装の SSoT)" {
  classify_paths <<<"dsl/domain-model.md"
  assert_scope 1 0
}

@test "pacts/ は backend (provider 検証が走る)" {
  classify_paths <<<"pacts/frontend-sales-management.json"
  assert_scope 1 0
}

@test "frontend ソースは frontend のみ" {
  classify_paths <<<"apps/frontend/src/pages/lots/LotListPage.tsx"
  assert_scope 0 1
}

@test "docs/ のみの変更は検証対象外" {
  classify_paths <<<"docs/design-note.md"
  assert_scope 0 0
}

@test "ルート直下の *.md は検証対象外" {
  classify_paths <<<"README.md"
  assert_scope 0 0
}

@test "未知パス (scripts/) は全検証 (fail-closed の要)" {
  classify_paths <<<"scripts/verify.sh"
  assert_scope 1 1
}

@test "未知パス (.github/) は全検証" {
  classify_paths <<<".github/workflows/verify.yml"
  assert_scope 1 1
}

@test "未知パス (.claude/) は全検証" {
  classify_paths <<<".claude/scripts/sarif-to-lessons.py"
  assert_scope 1 1
}

@test "backend + docs の混在は backend のみ" {
  classify_paths <<EOF
apps/api-fsharp/src/SalesManagement/Program.fs
docs/note.md
EOF
  assert_scope 1 0
}

@test "backend + frontend の混在は両スコープ" {
  classify_paths <<EOF
apps/api-fsharp/src/SalesManagement/Program.fs
apps/frontend/src/main.tsx
EOF
  assert_scope 1 1
}

@test "空行は無視される" {
  classify_paths <<EOF

docs/note.md

EOF
  assert_scope 0 0
}
