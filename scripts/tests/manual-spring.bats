#!/usr/bin/env bats
# Java は明示スコープ・手動 workflow だけで実行する。

setup() {
  cd "$BATS_TEST_DIRNAME/../.."
}

@test "自動判定は Java を起動しない" {
  run env VERIFY_SCOPE=auto VERIFY_DETECT_ONLY=1 bash scripts/verify.sh
  [ "$status" -eq 0 ]
  [[ "$output" == *'detect-only:'*'spring=0'* ]]
}

@test "手動 spring と all は Java を含む" {
  for scope in spring all; do
    run env VERIFY_SCOPE="$scope" VERIFY_DETECT_ONLY=1 bash scripts/verify.sh
    [ "$status" -eq 0 ]
    [[ "$output" == *'detect-only:'*'spring=1'* ]]
  done
}

@test "自動 verify に Java ジョブを置かず手動全量を既定にする" {
  run grep -E '^  spring:' .github/workflows/verify.yml
  [ "$status" -eq 1 ]
  workflow=$(<.github/workflows/spring-nightly.yml)
  [[ "$workflow" == *'default: full'* ]]
  [[ "$workflow" != *'  pull_request:'* ]]
  [[ "$workflow" != *'  push:'* ]]
  [[ "$workflow" != *'  schedule:'* ]]
}
