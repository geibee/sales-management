#!/usr/bin/env bash
# S3: ci.sh に Schemathesis を組み込み、SARIF が merged に統合される
. .ralph/verify/_common.sh

cd apps/api-fsharp
# ci.sh の中で必要な docker-compose / schemathesis 実行を含む。
# CI 設定の検証目的なのでローカルで bash ci.sh を回す。
ZAP_ENABLED=0 SCHEMATHESIS_ENABLED=1 bash ci.sh

test -f ci-results/sarif/schemathesis.sarif || { echo "FAIL: schemathesis.sarif missing"; exit 1; }
test -f ci-results/merged.sarif || { echo "FAIL: merged.sarif missing"; exit 1; }
grep -q "schemathesis" ci-results/merged.sarif || { echo "FAIL: schemathesis not in merged.sarif"; exit 1; }
echo "PASS: S3"
