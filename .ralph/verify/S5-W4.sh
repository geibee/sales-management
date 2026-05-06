#!/usr/bin/env bash
# S5-W4: 認証 / レート / OpenAPI 系 5 本を Support/* に移行
. .ralph/verify/_common.sh
. .ralph/verify/_wave_helpers.sh

WAVE_FILES=(
  apps/api-fsharp/tests/SalesManagement.Tests/IntegrationTests/JwtAuthenticationTests.fs
  apps/api-fsharp/tests/SalesManagement.Tests/IntegrationTests/RateLimitAndCacheTests.fs
  apps/api-fsharp/tests/SalesManagement.Tests/IntegrationTests/OpenApiSchemaTests.fs
  apps/api-fsharp/tests/SalesManagement.Tests/IntegrationTests/DevTokenMintTests.fs
  apps/api-fsharp/tests/SalesManagement.Tests/IntegrationTests/ConfigurationTests.fs
)
assert_wave_invariants
echo "PASS: S5-W4"
