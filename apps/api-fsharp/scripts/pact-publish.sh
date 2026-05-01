#!/usr/bin/env bash
# pacts/ 配下の Consumer Pact JSON を Pact Broker に公開する。
# 使い方:
#   bash scripts/pact-publish.sh
# 環境変数 (任意):
#   PACT_BROKER_URL  (default: http://localhost:9292)
#   PACT_BROKER_USER (default: pact)
#   PACT_BROKER_PASS (default: pact)
#   GIT_SHA          (default: $(git rev-parse --short HEAD))
set -euo pipefail

cd "$(dirname "$0")/../../.."   # sales-management/

BROKER_URL="${PACT_BROKER_URL:-http://localhost:9292}"
BROKER_USER="${PACT_BROKER_USER:-pact}"
BROKER_PASS="${PACT_BROKER_PASS:-pact}"
VERSION="${GIT_SHA:-$(git rev-parse --short HEAD 2>/dev/null || echo 'local')}"

if [ ! -d pacts ]; then
    echo "[pact-publish] pacts/ ディレクトリが存在しません" >&2
    exit 1
fi

echo "[pact-publish] broker=$BROKER_URL version=$VERSION"
docker run --rm --network host \
    -v "$PWD/pacts:/pacts:ro" \
    pactfoundation/pact-cli:latest \
    publish /pacts \
        --consumer-app-version="$VERSION" \
        --branch=main \
        --broker-base-url="$BROKER_URL" \
        --broker-username="$BROKER_USER" \
        --broker-password="$BROKER_PASS"
