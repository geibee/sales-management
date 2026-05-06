#!/usr/bin/env bash
# _generic.sh — project-local fallback for ralph-orchestrator.
# Used when a task has no `verify` field. Replaces the plugin's MoonBit default.
# Env: TASK_ID, BASELINE_TEST_COUNT, PLUGIN_ROOT
. .ralph/verify/_common.sh

dotnet test apps/api-fsharp/tests/SalesManagement.Tests \
  --filter "Category=Integration" --nologo

baseline="${BASELINE_TEST_COUNT:-0}"
current=$(dotnet test apps/api-fsharp/tests/SalesManagement.Tests \
            --no-restore --no-build --list-tests 2>/dev/null \
          | grep -cE '^    [A-Za-z]' || true)
[[ -z "$current" ]] && current=0
if (( current < baseline )); then
  echo "FAIL: test count regressed: $current < $baseline"
  exit 1
fi
echo "PASS: tests=$current (baseline=$baseline)"
