#!/usr/bin/env bash
# S2-Lot: POST /lots / GET /lots/{id} / GET /lots の境界値マトリクス
. .ralph/verify/_common.sh

dotnet test apps/api-fsharp/tests/SalesManagement.Tests \
  --filter "Category=Param&FullyQualifiedName~LotRoutesParamTests" --nologo

dotnet test apps/api-fsharp/tests/SalesManagement.Tests \
  --filter "Category=Integration" --nologo
echo "PASS: S2-Lot"
