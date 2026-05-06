#!/usr/bin/env bash
# S5-W6: Batch 系 9 本（個別判断、統合不可なら BatchFixture を別途用意）
. .ralph/verify/_common.sh
. .ralph/verify/_wave_helpers.sh

WAVE_FILES=(
  apps/api-fsharp/tests/SalesManagement.Tests/IntegrationTests/BatchMigrationTests.fs
  apps/api-fsharp/tests/SalesManagement.Tests/IntegrationTests/BatchJobLifecycleTests.fs
  apps/api-fsharp/tests/SalesManagement.Tests/IntegrationTests/BatchChunkProgressTests.fs
  apps/api-fsharp/tests/SalesManagement.Tests/IntegrationTests/BatchSkipPolicyTests.fs
  apps/api-fsharp/tests/SalesManagement.Tests/IntegrationTests/BatchPartitioningTests.fs
  apps/api-fsharp/tests/SalesManagement.Tests/IntegrationTests/BatchStateMachineTests.fs
  apps/api-fsharp/tests/SalesManagement.Tests/IntegrationTests/BatchJobLogFormatTests.fs
  apps/api-fsharp/tests/SalesManagement.Tests/IntegrationTests/BatchInputParserTests.fs
)
assert_wave_invariants

# S5-W6 完了時の最終チェック: HTTP を直接叩く既存テストが残っていないこと
remnants=$(grep -lrE 'TcpListener|new HttpClient' \
  apps/api-fsharp/tests/SalesManagement.Tests/IntegrationTests/ \
  | grep -v Support/ || true)
if [[ -n "$remnants" ]]; then
  echo "FAIL: tests still bypass Support/* HTTP helpers:"
  echo "$remnants"
  exit 1
fi
echo "PASS: S5-W6"
