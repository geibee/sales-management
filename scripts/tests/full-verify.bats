#!/usr/bin/env bats

setup() {
  cd "$BATS_TEST_DIRNAME/../.."
}

@test "F# 全量検証は migrations ディレクトリを解決できる場所から Migrator を起動する" {
  setup_line=$(grep "step setup 'Database setup'" scripts/full-verify.sh)

  [[ "$setup_line" == *"bash -c 'cd apps/api-fsharp && exec dotnet run --project tools/Migrator'"* ]]
}
