#!/usr/bin/env bash
# _common.sh — shared preamble sourced by every per-task verify script.
# Env (from orchestrator): TASK_ID, BASELINE_TEST_COUNT, PLUGIN_ROOT
# Cwd: worker's worktree (repo root within the worktree).
set -euo pipefail

echo "::: verify ${TASK_ID:-?} (baseline=${BASELINE_TEST_COUNT:-0})"

# Testcontainers requires Docker
if ! docker info >/dev/null 2>&1; then
  echo "FAIL: docker daemon not reachable (Testcontainers needs it)"
  exit 1
fi

# Build is the floor for any subsequent test invocation.
dotnet build apps/api-fsharp --nologo -v minimal
