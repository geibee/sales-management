#!/usr/bin/env bats

setup() {
  cd "$BATS_TEST_DIRNAME/../.."
}

@test "F# 全量検証は migrations ディレクトリを解決できる場所から Migrator を起動する" {
  setup_line=$(grep "step setup 'Database setup'" scripts/full-verify.sh)

  [[ "$setup_line" == *"bash -c 'cd apps/api-fsharp && exec dotnet run --project tools/Migrator'"* ]]
}

@test "全量検証は PR checkout でも解決できる origin/main を既定基準にする" {
  run grep -F 'export VERIFY_BASE_REF="${VERIFY_BASE_REF:-origin/main}"' scripts/full-verify.sh

  [ "$status" -eq 0 ]
}
