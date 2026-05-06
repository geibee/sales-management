#!/usr/bin/env bash
# S5-W3: SalesCase 系 5 本を Support/* に移行
. .ralph/verify/_common.sh
. .ralph/verify/_wave_helpers.sh

WAVE_FILES=(
  apps/api-fsharp/tests/SalesManagement.Tests/IntegrationTests/SalesCaseRetrievalTests.fs
  apps/api-fsharp/tests/SalesManagement.Tests/IntegrationTests/SalesCaseDetailTests.fs
  apps/api-fsharp/tests/SalesManagement.Tests/IntegrationTests/SalesCaseProblemDetailsTests.fs
  apps/api-fsharp/tests/SalesManagement.Tests/IntegrationTests/SalesCaseSubtypeRoutingTests.fs
  apps/api-fsharp/tests/SalesManagement.Tests/IntegrationTests/ListEndpointsTests.fs
)
assert_wave_invariants
echo "PASS: S5-W3"
