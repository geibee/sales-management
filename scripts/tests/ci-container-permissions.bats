#!/usr/bin/env bats

setup() {
  CI_SCRIPT="$BATS_TEST_DIRNAME/../../apps/api-fsharp/ci.sh"
}

@test "ZAP コンテナは固定イメージを既定ユーザーで実行する" {
  zap_block=$(sed -n '/^if \[ "$ZAP_ENABLED" = "1" \]; then$/,/^else$/p' "$CI_SCRIPT")

  [[ "$zap_block" != *'--user "$(id -u):$(id -g)"'* ]]
  [[ "$zap_block" != *'HOME=/tmp'* ]]
  [[ "$zap_block" == *'ghcr.io/zaproxy/zaproxy:2.17.0@sha256:8d387b1a63e3425beef4846e39719f5af2a787753af2d8b6558c6257d7a577a2'* ]]
}

@test "ZAP コンテナに専用書込領域だけを渡し起動待機を制限する" {
  zap_block=$(sed -n '/^if \[ "$ZAP_ENABLED" = "1" \]; then$/,/^else$/p' "$CI_SCRIPT")

  [[ "$zap_block" == *'mkdir -p "$RESULTS_DIR/zap-wrk"'* ]]
  [[ "$zap_block" == *'chmod 0777 "$RESULTS_DIR/zap-wrk"'* ]]
  [[ "$zap_block" == *'-v "$PWD/$RESULTS_DIR/zap-wrk:/zap/wrk:rw"'* ]]
  [[ "$zap_block" == *'-w /zap/wrk'* ]]
  [[ "$zap_block" == *'-T 5'* ]]
}

@test "ZAP は終了コード 0 と 2 だけを成功扱いする" {
  [[ $(<"$CI_SCRIPT") == *'if [ "$ZAP_EXIT" -ne 0 ] && [ "$ZAP_EXIT" -ne 2 ]; then'* ]]
}

@test "Schemathesis コンテナは固定イメージを tmp で実行する" {
  schemathesis_block=$(sed -n '/^if \[ "$SCHEMATHESIS_ENABLED" = "1" \]; then$/,/^else$/p' "$CI_SCRIPT")

  [[ "$schemathesis_block" == *'schemathesis/schemathesis:4.24.3@sha256:dd1ebf7519958c34c276a65c20f9f2f808dbefb06c86163eb284ff5674c6a9f3'* ]]
  [[ "$schemathesis_block" == *'-w /tmp'* ]]
}
