#!/usr/bin/env bash
# 生成コードドリフト検査 — src/contracts/generated.ts が openapi.yaml と同期しているか
#
# `pnpm gen:api` と同じ生成を一時ファイルに対して行い、コミット済みの
# generated.ts と diff する。作業ツリーは汚さない (fail-closed: 生成に失敗したら
# 「差分なし」ではなくエラー終了する)。
#
# 同期が壊れるケース: openapi.yaml だけ直して gen:api を忘れた /
# generated.ts を手で編集した / openapi-zod-client のバージョンが変わった
set -euo pipefail
cd "$(dirname "$0")/.."

COMMITTED="src/contracts/generated.ts"
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

pnpm exec openapi-zod-client ../api-fsharp/openapi.yaml -o "$tmp/generated.ts" >/dev/null

if ! diff -u "$COMMITTED" "$tmp/generated.ts"; then
  {
    echo ""
    echo "[contract-drift] FAIL: $COMMITTED が openapi.yaml と同期していません。"
    echo "[contract-drift] 'pnpm gen:api' を実行して再生成結果をコミットしてください。"
  } >&2
  exit 1
fi

echo "[contract-drift] PASS: generated.ts は openapi.yaml と同期しています"
