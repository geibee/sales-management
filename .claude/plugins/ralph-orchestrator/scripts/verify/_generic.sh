#!/usr/bin/env bash
# _generic.sh — default verify: dotnet build + fantomas + dotnet test, test count >= baseline
# Env: TASK_ID, BASELINE_TEST_COUNT, PLUGIN_ROOT
# 本リポジトリ (F#/.NET) 向け。他構成のプロジェクトは <repo>/.ralph/verify/_generic.sh で上書きする

set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

echo "::: generic verify for ${TASK_ID:-?} (baseline tests: ${BASELINE_TEST_COUNT:-0})"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet CLI not found. Skipping .NET checks."
  exit 0
fi

# fantomas はローカルツール (apps/api-fsharp/dotnet-tools.json)。ci.sh と同じく app ディレクトリで実行する
cd apps/api-fsharp

dotnet build src/SalesManagement --warnaserror
dotnet build tests/SalesManagement.Tests --warnaserror
dotnet fantomas --check src/ tests/

out=$(dotnet test tests/SalesManagement.Tests 2>&1) || { echo "$out"; exit 1; }
echo "$out"

# VSTest 形式 "Passed: NN" / MTP 形式 "Total tests: NN" の両方に対応
current=$(echo "$out" | grep -oE 'Passed:[[:space:]]*[0-9]+' | grep -oE '[0-9]+' | tail -1 || echo 0)
[[ -z "$current" ]] && current=0
if [[ "$current" == "0" ]]; then
  current=$(echo "$out" | grep -oE 'Total tests: [0-9]+' | grep -oE '[0-9]+' | tail -1 || echo 0)
  [[ -z "$current" ]] && current=0
fi
baseline="${BASELINE_TEST_COUNT:-0}"

if (( current < baseline )); then
  echo "FAIL: test count regressed: $current < $baseline"
  exit 1
fi
echo "PASS: tests passed: $current (baseline $baseline)"
