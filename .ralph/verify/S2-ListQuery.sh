#!/usr/bin/env bash
# S2-ListQuery: GET /lots, GET /sales-cases のクエリパラメータ全組合せ
. .ralph/verify/_common.sh

dotnet test apps/api-fsharp/tests/SalesManagement.Tests \
  --filter "Category=Param&FullyQualifiedName~ListQueryParamTests" --nologo

dotnet test apps/api-fsharp/tests/SalesManagement.Tests \
  --filter "Category=Integration" --nologo
echo "PASS: S2-ListQuery"
