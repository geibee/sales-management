#!/usr/bin/env bash
# DSL parser CI: pytest を回し、AST スナップショットの安定性を検証する。
set -euo pipefail

cd "$(dirname "$0")"

echo "=== uv sync ==="
uv sync --frozen

echo "=== pytest ==="
uv run pytest -v

echo "=== domain-model.md パース可能性 ==="
uv run dsl-parser ../../dsl/domain-model.md > /dev/null
echo "OK"

echo "=== domain-model.md spec生成 ==="
spec_json="$(mktemp)"
trap 'rm -f "$spec_json"' EXIT
uv run dsl-parser --format spec ../../dsl/domain-model.md > "$spec_json"
echo "OK"

echo "=== spec/F# 実装照合リンター ==="
uv run dsl-spec-lint \
  --spec "$spec_json" \
  --domain-dir ../../apps/api-fsharp/src/SalesManagement/Domain \
  --glossary ../../glossary.yaml
