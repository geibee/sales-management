#!/usr/bin/env bash
# S2-StateTrans: state transition 系（version + date）のパラメータマトリクス
. .ralph/verify/_common.sh

dotnet test apps/api-fsharp/tests/SalesManagement.Tests \
  --filter "Category=Param&FullyQualifiedName~StateTransitionParamTests" --nologo

dotnet test apps/api-fsharp/tests/SalesManagement.Tests \
  --filter "Category=Integration" --nologo
echo "PASS: S2-StateTrans"
