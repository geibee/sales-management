#!/usr/bin/env bash
# S5-W5: Outbox / 外部 API / 観測 3 本を Support/* に移行
. .ralph/verify/_common.sh
. .ralph/verify/_wave_helpers.sh

WAVE_FILES=(
  apps/api-fsharp/tests/SalesManagement.Tests/IntegrationTests/OutboxTests.fs
  apps/api-fsharp/tests/SalesManagement.Tests/IntegrationTests/ExternalPricingTests.fs
  apps/api-fsharp/tests/SalesManagement.Tests/IntegrationTests/AuditAndOtelTests.fs
)
assert_wave_invariants
echo "PASS: S5-W5"
