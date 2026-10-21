#!/usr/bin/env bats
# GitHub hosted runner の APT ミラー障害で Playwright の依存導入が停止しないよう、
# frontend verify はブラウザと OS 依存を同梱した公式イメージを使用する。

setup() {
  FRONTEND_LOCKFILE="$BATS_TEST_DIRNAME/../../apps/frontend/pnpm-lock.yaml"
  VERIFY_WORKFLOW="$BATS_TEST_DIRNAME/../../.github/workflows/verify.yml"
  NIGHTLY_WORKFLOW="$BATS_TEST_DIRNAME/../../.github/workflows/nightly.yml"
}

@test "frontend verify の Playwright イメージは lockfile と同じバージョンに固定する" {
  playwright_version=$(sed -n "s/^  '@playwright\/test@\([^']*\)':$/\1/p" "$FRONTEND_LOCKFILE" | head -n 1)
  workflow=$(<"$VERIFY_WORKFLOW")

  [[ -n "$playwright_version" ]]
  [[ "$workflow" == *"image: mcr.microsoft.com/playwright:v${playwright_version}-noble"* ]]
}

@test "frontend verify は Playwright の OS 依存を APT で再導入しない" {
  workflow=$(<"$VERIFY_WORKFLOW")

  [[ "$workflow" != *'playwright install --with-deps'* ]]
}

@test "nightly heavy は Playwright 対応の Ubuntu 24.04 に固定する" {
  run awk '/^  heavy:$/ { in_job=1; next } /^  [^ ]/ { in_job=0 } in_job && /runs-on:/ { print $2 }' "$NIGHTLY_WORKFLOW"
  [ "$status" -eq 0 ]
  [ "$output" = "ubuntu-24.04" ]
}

@test "nightly E2E は Playwright 対応の Ubuntu 24.04 に固定する" {
  run awk '/^  e2e:$/ { in_job=1; next } /^  [^ ]/ { in_job=0 } in_job && /runs-on:/ { print $2 }' "$NIGHTLY_WORKFLOW"
  [ "$status" -eq 0 ]
  [ "$output" = "ubuntu-24.04" ]
}
