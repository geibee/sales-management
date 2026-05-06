#!/usr/bin/env bash
# S5-W1: Health/Auth/CORS/Security/Misc を Support/* に移行
. .ralph/verify/_common.sh
. .ralph/verify/_wave_helpers.sh

WAVE_FILES=(
  apps/api-fsharp/tests/SalesManagement.Tests/IntegrationTests/HealthAndOpenApiTests.fs
  apps/api-fsharp/tests/SalesManagement.Tests/IntegrationTests/AuthConfigEndpointTests.fs
  apps/api-fsharp/tests/SalesManagement.Tests/IntegrationTests/CorsTests.fs
  apps/api-fsharp/tests/SalesManagement.Tests/IntegrationTests/SecurityHeaderTests.fs
  apps/api-fsharp/tests/SalesManagement.Tests/IntegrationTests/MiscEndpointTests.fs
)
assert_wave_invariants
echo "PASS: S5-W1"
