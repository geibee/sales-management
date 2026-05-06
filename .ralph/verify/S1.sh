#!/usr/bin/env bash
# S1: Support/* 共通ハーネス導入 + スモークテスト
# 期待: Smoke テストが緑、既存 Integration テストが退行していない
. .ralph/verify/_common.sh

# 1) Support/* が fsproj の Compile 順で先頭に来ているか
fsproj=apps/api-fsharp/tests/SalesManagement.Tests/SalesManagement.Tests.fsproj
first_compile=$(grep -E '<Compile Include=' "$fsproj" | head -1)
echo "first compile: $first_compile"
if ! echo "$first_compile" | grep -q 'Support/'; then
  echo "FAIL: Support/* must be the first Compile entry in $fsproj"
  exit 1
fi

# 2) Smoke カテゴリのテストが少なくとも 1 本存在し緑
dotnet test apps/api-fsharp/tests/SalesManagement.Tests \
  --filter "Category=Smoke" --nologo

# 3) 既存 Integration テストの退行なし
dotnet test apps/api-fsharp/tests/SalesManagement.Tests \
  --filter "Category=Integration" --nologo
echo "PASS: S1"
