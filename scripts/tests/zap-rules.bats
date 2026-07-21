#!/usr/bin/env bats

setup() {
  ZAP_RULES="$BATS_TEST_DIRNAME/../../apps/api-fsharp/zap-rules.tsv"
}

@test "localhost の nightly DAST は HTTP-only 警告を抑止する" {
  run grep -F $'10106\tIGNORE\thttp://localhost:5000/.*' "$ZAP_RULES"

  [ "$status" -eq 0 ]
}

@test "CSV ダウンロードは Persistent XSS のブラウザ実行文脈から除外する" {
  run grep -F $'40014\tIGNORE\t.*/lots/export.*' "$ZAP_RULES"

  [ "$status" -eq 0 ]
}

@test "DB 識別子だけを受け取るロット作成は Path Traversal から除外する" {
  run grep -F $'6\tIGNORE\t.*/lots$' "$ZAP_RULES"

  [ "$status" -eq 0 ]
}
