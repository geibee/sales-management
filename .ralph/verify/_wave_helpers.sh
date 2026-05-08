#!/usr/bin/env bash
# _wave_helpers.sh — sourced by S5-W*.sh after _common.sh.
# Provides assert_wave_invariants(): test count preserved, no orphan helpers.
# Caller sets: WAVE_FILES=(absolute or relative paths to migrated test files).
set -euo pipefail

assert_wave_invariants() {
  local baseline="${BASELINE_TEST_COUNT:-0}"
  # 1) Test count must equal baseline (rewrites should not add/remove cases).
  #    Fall back to recomputing on the fly if orchestrator failed to capture.
  if [[ "$baseline" -eq 0 ]]; then
    echo "INFO: BASELINE_TEST_COUNT=0, recomputing from .ralph/capture-baseline.sh"
    baseline=$(bash .ralph/capture-baseline.sh 2>/dev/null || echo 0)
  fi
  if [[ "$baseline" -eq 0 ]]; then
    echo "FAIL: baseline test count is 0 — gate disabled, refusing to proceed"
    return 1
  fi
  local current
  current=$(dotnet test apps/api-fsharp/tests/SalesManagement.Tests \
              --no-restore --no-build --list-tests 2>/dev/null \
            | grep -cE '^    [A-Za-z]' || true)
  [[ -z "$current" ]] && current=0
  if [[ "$current" -ne "$baseline" ]]; then
    echo "FAIL: test count drift: $current != $baseline"
    return 1
  fi

  # 2) Per-file helper residues must be gone (those should live in Support/*)
  local orphan_pattern='let private (getFreePort|newClient|postJson|putJson|parseJson|readBody|deleteWithBody|createLotBody|createSalesCaseBody)\b'
  for f in "${WAVE_FILES[@]}"; do
    if [[ -f "$f" ]] && grep -qE "$orphan_pattern" "$f"; then
      echo "FAIL: orphan helper still present in $f:"
      grep -nE "$orphan_pattern" "$f"
      return 1
    fi
  done

  # 3) Positive check: migrated files must actually use Support/* (not just
  #    inline TcpListener/HttpClient directly). Without this, a worker can
  #    "pass" by deleting helper definitions and inlining their bodies.
  for f in "${WAVE_FILES[@]}"; do
    [[ -f "$f" ]] || continue
    if grep -qE 'TcpListener|new HttpClient' "$f" \
       && ! grep -qE 'open SalesManagement\.Tests\.Support|Support\.(ApiFixture|HttpHelpers|RequestBuilders|BatchFixture)' "$f"; then
      echo "FAIL: $f uses TcpListener/HttpClient directly without importing Support/*:"
      grep -nE 'TcpListener|new HttpClient' "$f" | head -5
      return 1
    fi
  done

  # 4) All Integration tests green
  dotnet test apps/api-fsharp/tests/SalesManagement.Tests \
    --filter "Category=Integration" --nologo
}
