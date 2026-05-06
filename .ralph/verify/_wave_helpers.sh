#!/usr/bin/env bash
# _wave_helpers.sh — sourced by S5-W*.sh after _common.sh.
# Provides assert_wave_invariants(): test count preserved, no orphan helpers.
# Caller sets: WAVE_FILES=(absolute or relative paths to migrated test files).
set -euo pipefail

assert_wave_invariants() {
  local baseline="${BASELINE_TEST_COUNT:-0}"
  # 1) Test count must equal baseline (rewrites should not add/remove cases)
  local current
  current=$(dotnet test apps/api-fsharp/tests/SalesManagement.Tests \
              --no-restore --no-build --list-tests 2>/dev/null \
            | grep -cE '^    [A-Za-z]' || true)
  [[ -z "$current" ]] && current=0
  if [[ "$baseline" -eq 0 ]]; then
    echo "WARN: BASELINE_TEST_COUNT=0; capture-baseline.sh may have failed"
  elif [[ "$current" -ne "$baseline" ]]; then
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

  # 3) All Integration tests green
  dotnet test apps/api-fsharp/tests/SalesManagement.Tests \
    --filter "Category=Integration" --nologo
}
