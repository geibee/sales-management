#!/usr/bin/env bats
# GitHub hosted runner の APT ミラー障害で Playwright の依存導入が停止しないよう、
# frontend verify はブラウザと OS 依存を同梱した公式イメージを使用する。

setup() {
  FRONTEND_LOCKFILE="$BATS_TEST_DIRNAME/../../apps/frontend/pnpm-lock.yaml"
  VERIFY_WORKFLOW="$BATS_TEST_DIRNAME/../../.github/workflows/verify.yml"
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
