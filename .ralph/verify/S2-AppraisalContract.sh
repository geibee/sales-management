#!/usr/bin/env bash
# S2-AppraisalContract: appraisal/contract 系のパラメータマトリクス
. .ralph/verify/_common.sh

dotnet test apps/api-fsharp/tests/SalesManagement.Tests \
  --filter "Category=Param&FullyQualifiedName~AppraisalContractParamTests" --nologo

dotnet test apps/api-fsharp/tests/SalesManagement.Tests \
  --filter "Category=Integration" --nologo
echo "PASS: S2-AppraisalContract"
