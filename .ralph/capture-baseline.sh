#!/usr/bin/env bash
# capture-baseline.sh — count integration tests for ralph-orchestrator's
# baseline_test_count. Override of plugin's MoonBit-based capture.
# Called from $PROJECT_ROOT (cwd is repo root).
# Stdout: integer count (single line).
set -euo pipefail

# `dotnet test --list-tests` produces lines like "    SalesManagement.Tests.Foo.Bar".
# Filter by 4-leading-spaces; count.
count=$(dotnet test apps/api-fsharp/tests/SalesManagement.Tests \
          --no-restore --no-build --list-tests 2>/dev/null \
        | grep -cE '^    [A-Za-z]' || true)

# If --no-build path failed (fresh worktree), do a full list with build.
if [[ -z "$count" || "$count" == "0" ]]; then
  count=$(dotnet test apps/api-fsharp/tests/SalesManagement.Tests \
            --list-tests 2>/dev/null \
          | grep -cE '^    [A-Za-z]' || true)
fi

[[ -z "$count" ]] && count=0
echo "$count"
