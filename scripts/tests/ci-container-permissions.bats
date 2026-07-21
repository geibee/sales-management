#!/usr/bin/env bats

setup() {
  CI_SCRIPT="$BATS_TEST_DIRNAME/../../apps/api-fsharp/ci.sh"
}

@test "ZAP コンテナはホスト UID:GID で成果物を書き込む" {
  zap_block=$(sed -n '/^if \[ "$ZAP_ENABLED" = "1" \]; then$/,/^else$/p' "$CI_SCRIPT")

  [[ "$zap_block" == *'--user "$(id -u):$(id -g)"'* ]]
  [[ "$zap_block" == *'-e HOME=/tmp'* ]]
  [[ "$zap_block" == *'-v "$PWD/$RESULTS_DIR:/zap/wrk:rw"'* ]]
}
