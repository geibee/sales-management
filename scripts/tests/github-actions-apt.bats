#!/usr/bin/env bats
# GitHub hosted runner の Azure APT ミラーが停止しても CI が待ち続けないよう、
# APT を使う全 workflow は共通の安定化スクリプトを先に実行する。

setup() {
  APT_SCRIPT="$BATS_TEST_DIRNAME/../configure-ubuntu-apt.sh"
  VERIFY_WORKFLOW="$BATS_TEST_DIRNAME/../../.github/workflows/verify.yml"
  NIGHTLY_WORKFLOW="$BATS_TEST_DIRNAME/../../.github/workflows/nightly.yml"
  SPRING_NIGHTLY_WORKFLOW="$BATS_TEST_DIRNAME/../../.github/workflows/spring-nightly.yml"
}

@test "APT 安定化スクリプトは HTTPS ミラーと有限の取得待機を設定する" {
  script=$(<"$APT_SCRIPT")

  [[ "$script" == *"https://archive.ubuntu.com/ubuntu"* ]]
  [[ "$script" == *"/etc/apt/apt-mirrors.txt"* ]]
  [[ "$script" == *'Acquire::Retries "3"'* ]]
  [[ "$script" == *'Acquire::http::Timeout "20"'* ]]
  [[ "$script" == *'Acquire::https::Timeout "20"'* ]]
}

@test "APT を使う workflow は共通の安定化スクリプトを実行する" {
  for workflow_path in \
    "$VERIFY_WORKFLOW" \
    "$NIGHTLY_WORKFLOW" \
    "$SPRING_NIGHTLY_WORKFLOW"; do
    workflow=$(<"$workflow_path")

    [[ "$workflow" == *'sudo bash scripts/configure-ubuntu-apt.sh'* ]]
  done
}

@test "nightly は APT 安定化後に Playwright の OS 依存を導入する" {
  for workflow_path in "$NIGHTLY_WORKFLOW" "$SPRING_NIGHTLY_WORKFLOW"; do
    configure_line=$(grep -nF 'sudo bash scripts/configure-ubuntu-apt.sh' "$workflow_path" | head -n 1)
    playwright_line=$(grep -nF 'playwright install --with-deps chromium' "$workflow_path" | head -n 1)

    [[ -n "$configure_line" ]]
    [[ -n "$playwright_line" ]]
    (( ${configure_line%%:*} < ${playwright_line%%:*} ))
  done
}
