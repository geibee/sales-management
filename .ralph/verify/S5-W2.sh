#!/usr/bin/env bash
# S5-W2: Lot 系 5 本を Support/* に移行
. .ralph/verify/_common.sh
. .ralph/verify/_wave_helpers.sh

WAVE_FILES=(
  apps/api-fsharp/tests/SalesManagement.Tests/IntegrationTests/LotLifecycleTests.fs
  apps/api-fsharp/tests/SalesManagement.Tests/IntegrationTests/LotErrorHandlingTests.fs
  apps/api-fsharp/tests/SalesManagement.Tests/IntegrationTests/LotCsvExportTests.fs
  apps/api-fsharp/tests/SalesManagement.Tests/IntegrationTests/LotIdMigrationTests.fs
  apps/api-fsharp/tests/SalesManagement.Tests/IntegrationTests/OptimisticLockConflictTests.fs
)
assert_wave_invariants
echo "PASS: S5-W2"
