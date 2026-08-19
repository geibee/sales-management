#!/usr/bin/env bats
# GitHub hosted runner の Azure APT ミラーが停止しても verify 全体が待ち続けないよう、
# repo 共通ジョブは安定した HTTPS ミラーと有限の取得待機を使用する。

setup() {
  VERIFY_WORKFLOW="$BATS_TEST_DIRNAME/../../.github/workflows/verify.yml"
}

@test "repo verify は Ubuntu の APT 取得先を HTTPS ミラーに固定する" {
  workflow=$(<"$VERIFY_WORKFLOW")

  [[ "$workflow" == *"https://archive.ubuntu.com/ubuntu"* ]]
  [[ "$workflow" == *"/etc/apt/apt-mirrors.txt"* ]]
}

@test "repo verify の APT 取得には再試行回数とタイムアウトを設定する" {
  workflow=$(<"$VERIFY_WORKFLOW")

  [[ "$workflow" == *'Acquire::Retries=3'* ]]
  [[ "$workflow" == *'Acquire::http::Timeout=20'* ]]
  [[ "$workflow" == *'Acquire::https::Timeout=20'* ]]
}
