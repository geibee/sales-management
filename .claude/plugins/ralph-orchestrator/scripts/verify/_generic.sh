#!/usr/bin/env bash
# _generic.sh — default verify: moon check + moon test, test count >= baseline
# Env: TASK_ID, BASELINE_TEST_COUNT, PLUGIN_ROOT

set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

echo "::: generic verify for ${TASK_ID:-?} (baseline tests: ${BASELINE_TEST_COUNT:-0})"

if ! command -v moon >/dev/null 2>&1; then
  echo "moon CLI not found. Skipping moon-specific checks."
  exit 0
fi

moon check
out=$(moon test 2>&1) || { echo "$out"; exit 1; }
echo "$out"

current=$(echo "$out" | grep -oE 'passed: [0-9]+' | grep -oE '[0-9]+' | tail -1 || echo 0)
[[ -z "$current" ]] && current=0
baseline="${BASELINE_TEST_COUNT:-0}"

if (( current < baseline )); then
  echo "FAIL: test count regressed: $current < $baseline"
  exit 1
fi
echo "PASS: tests passed: $current (baseline $baseline)"
