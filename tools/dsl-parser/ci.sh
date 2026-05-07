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
