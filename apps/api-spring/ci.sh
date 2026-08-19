#!/usr/bin/env bash
# Spring 専用の手動 nightly。必須 tool・service・report が一つでも欠ければ失敗する。
set -euo pipefail

cd "$(dirname "$0")"

RESULTS_DIR="$PWD/ci-results"
SARIF_DIR="$RESULTS_DIR/sarif"
# Trivy は Java 依存の POM を標準の ~/.m2/repository から探索する。
# Maven も同じ場所を使い、ビルドで解決済みの依存を脆弱性スキャンで再利用する。
MAVEN_REPOSITORY="${HOME:?HOME が設定されていません}/.m2/repository"
PACT_BROKER_URL="${PACT_BROKER_URL:?PACT_BROKER_URL は必須です}"
PACT_BROKER_USERNAME="${PACT_BROKER_USERNAME:?PACT_BROKER_USERNAME は必須です}"
PACT_BROKER_PASSWORD="${PACT_BROKER_PASSWORD:?PACT_BROKER_PASSWORD は必須です}"
SPRING_API_PORT="${SPRING_API_PORT:-8080}"
SPRING_MANAGEMENT_PORT="${SPRING_MANAGEMENT_PORT:-8081}"
FSHARP_API_PORT="${FSHARP_API_PORT:-5001}"
EXTERNAL_PRICING_BASE_URL="${EXTERNAL_PRICING_BASE_URL:-http://localhost:8089}"
FSHARP_DATABASE_CONNECTION_STRING="${FSHARP_DATABASE_CONNECTION_STRING:-Host=localhost;Port=5432;Database=sales_management_fsharp_parity;Username=app;Password=app}"
FSHARP_PARITY_DATABASE_URL="${FSHARP_PARITY_DATABASE_URL:-postgresql://app:app@localhost:5432/sales_management_fsharp_parity}"
SPRING_PARITY_DATABASE_URL="${SPRING_PARITY_DATABASE_URL:-postgresql://app:app@localhost:5432/sales_management}"
ZAP_IMAGE="ghcr.io/zaproxy/zaproxy:2.17.0@sha256:8d387b1a63e3425beef4846e39719f5af2a787753af2d8b6558c6257d7a577a2"
SCHEMATHESIS_IMAGE="schemathesis/schemathesis:4.24.3@sha256:dd1ebf7519958c34c276a65c20f9f2f808dbefb06c86163eb284ff5674c6a9f3"
APP_PID=""
FSHARP_PID=""

finish() {
  if [[ -n "$FSHARP_PID" ]]; then
    kill "$FSHARP_PID" 2>/dev/null || true
    wait "$FSHARP_PID" 2>/dev/null || true
  fi
  if [[ -n "$APP_PID" ]]; then
    kill "$APP_PID" 2>/dev/null || true
    wait "$APP_PID" 2>/dev/null || true
  fi
}
trap finish EXIT

for tool in java dotnet python3 psql docker curl gitleaks trivy pnpm; do
  command -v "$tool" >/dev/null 2>&1 || {
    echo "必須 tool がありません: $tool" >&2
    exit 1
  }
done
[[ -x ./mvnw ]] || { echo "Maven Wrapper が実行できません" >&2; exit 1; }

rm -rf "$RESULTS_DIR"
mkdir -p "$SARIF_DIR" "$MAVEN_REPOSITORY"

echo "=== 軽量ゲート + package ==="
python3 scripts/verify-contracts.py
./mvnw -B \
  -Dmaven.repo.local="$MAVEN_REPOSITORY" \
  -Pstatic-analysis,nightly \
  com.diffplug.spotless:spotless-maven-plugin:3.3.0:check install
python3 scripts/verify-quality-ratchets.py

echo "=== SpotBugs ==="
./mvnw -B \
  -Dmaven.repo.local="$MAVEN_REPOSITORY" \
  -Pnightly -Dspotbugs.xmlOutput=true spotbugs:spotbugs
python3 scripts/report-to-sarif.py \
  --tool SpotBugs --kind spotbugs \
  --input '*/target/spotbugsXml.xml' \
  --output "$SARIF_DIR/spotbugs.sarif"

echo "=== PIT mutation ==="
for module in domain application; do
  ./mvnw -B \
    -Dmaven.repo.local="$MAVEN_REPOSITORY" \
    -Pnightly -f "$module/pom.xml" \
    org.pitest:pitest-maven:mutationCoverage
done
python3 scripts/report-to-sarif.py \
  --tool PIT --kind pit \
  --input '*/target/pit-reports/mutations.xml' \
  --output "$SARIF_DIR/pit.sarif"
python3 scripts/verify-quality-ratchets.py --require-mutation

echo "=== gitleaks / Trivy ==="
(
  cd ../..
  gitleaks detect --source . --no-banner --redact \
    --report-format sarif \
    --report-path "$SARIF_DIR/gitleaks.sarif"
)
trivy fs --scanners vuln,secret,misconfig --severity HIGH,CRITICAL \
  --exit-code 1 --format sarif --output "$SARIF_DIR/trivy.sarif" .

echo "=== CycloneDX SBOM ==="
./mvnw -B \
  -Dmaven.repo.local="$MAVEN_REPOSITORY" \
  -Pnightly org.cyclonedx:cyclonedx-maven-plugin:makeAggregateBom
cp target/bom.json "$RESULTS_DIR/sbom-spring.cdx.json"
python3 scripts/report-to-sarif.py \
  --tool CycloneDX --kind artifact \
  --input "$RESULTS_DIR/sbom-spring.cdx.json" \
  --output "$SARIF_DIR/cyclonedx.sarif"

echo "=== Pact Broker ==="
curl -fsS -u "$PACT_BROKER_USERNAME:$PACT_BROKER_PASSWORD" \
  "$PACT_BROKER_URL/diagnostic/status/heartbeat" >/dev/null
PACT_VERSION="${GITHUB_SHA:-local}"
PACT_URL="$PACT_BROKER_URL/pacts/provider/sales-management/consumer/frontend/version/$PACT_VERSION"
curl -fsS -u "$PACT_BROKER_USERNAME:$PACT_BROKER_PASSWORD" \
  -X PUT -H 'Content-Type: application/json' \
  --data-binary @../../pacts/frontend-sales-management.json "$PACT_URL" >/dev/null
curl -fsS -u "$PACT_BROKER_USERNAME:$PACT_BROKER_PASSWORD" \
  -H 'Accept: application/hal+json' "$PACT_URL" >"$RESULTS_DIR/pact.json"
python3 scripts/report-to-sarif.py \
  --tool 'Pact Broker' --kind artifact \
  --input "$RESULTS_DIR/pact.json" \
  --output "$SARIF_DIR/pact.sarif"

echo "=== API 起動 ==="
EXTERNAL_PRICING_BASE_URL="$EXTERNAL_PRICING_BASE_URL" \
  RATE_LIMIT_PERMIT_LIMIT=100000 PORT="$SPRING_API_PORT" \
  MANAGEMENT_PORT="$SPRING_MANAGEMENT_PORT" \
  java -jar api/target/api-0.1.0-SNAPSHOT.jar \
  --sales-management.outbox.enabled=false >"$RESULTS_DIR/api.log" 2>&1 &
APP_PID=$!
for _ in $(seq 1 60); do
  curl -fsS --max-time 2 "http://localhost:$SPRING_API_PORT/health" >/dev/null 2>&1 && break
  sleep 1
done
curl -fsS --max-time 2 "http://localhost:$SPRING_API_PORT/health" >/dev/null

echo "=== F# / Spring parity (35 operations + DB) ==="
(
  cd ../api-fsharp
  dotnet build SalesManagement.slnx --warnaserror
  DATABASE_URL="$FSHARP_DATABASE_CONNECTION_STRING" \
    dotnet run --no-build --project tools/Migrator
)
(
  cd ../api-fsharp
    exec env \
      DATABASE_URL="$FSHARP_DATABASE_CONNECTION_STRING" \
      Server__Port="$FSHARP_API_PORT" \
      ExternalApi__PricingUrl="$EXTERNAL_PRICING_BASE_URL" \
      Outbox__PollIntervalMs=600000 \
      RateLimit__PermitLimit=100000 \
      dotnet run --no-build --project src/SalesManagement \
      >"$RESULTS_DIR/fsharp-api.log" 2>&1
) &
FSHARP_PID=$!
for _ in $(seq 1 60); do
  curl -fsS --max-time 2 "http://localhost:$FSHARP_API_PORT/health" >/dev/null 2>&1 && break
  sleep 1
done
curl -fsS --max-time 2 "http://localhost:$FSHARP_API_PORT/health" >/dev/null
python3 scripts/parity-check.py \
  --fsharp-url "http://localhost:$FSHARP_API_PORT" \
  --spring-url "http://localhost:$SPRING_API_PORT" \
  --fsharp-database-url "$FSHARP_PARITY_DATABASE_URL" \
  --spring-database-url "$SPRING_PARITY_DATABASE_URL" \
  --output "$RESULTS_DIR/parity-transcript.json"
python3 scripts/report-to-sarif.py \
  --tool 'F#/Spring parity' --kind artifact \
  --input "$RESULTS_DIR/parity-transcript.json" \
  --output "$SARIF_DIR/parity.sarif"

echo "=== OWASP ZAP ==="
mkdir -p "$RESULTS_DIR/zap-wrk"
chmod 0777 "$RESULTS_DIR/zap-wrk"
set +e
docker run --rm --network host \
  -v "$PWD/../api-fsharp/openapi.yaml:/zap/openapi.yaml:ro" \
  -v "$RESULTS_DIR/zap-wrk:/zap/wrk:rw" \
  -w /zap/wrk "$ZAP_IMAGE" \
  zap-api-scan.py -t /zap/openapi.yaml -f openapi \
  -O "http://localhost:$SPRING_API_PORT" \
  -J zap-report.json -r zap-report.html -w zap-report.md \
  -z '-config api.disablekey=true -addonuninstall domxss' -T 5 -l WARN \
  2>&1 | tee "$RESULTS_DIR/zap.log"
ZAP_EXIT=${PIPESTATUS[0]}
set -e
[[ "$ZAP_EXIT" -eq 0 || "$ZAP_EXIT" -eq 2 ]] || {
  echo "ZAP 実行失敗: exit=$ZAP_EXIT" >&2
  exit 1
}
for report in zap-report.json zap-report.html zap-report.md; do
  [[ -s "$RESULTS_DIR/zap-wrk/$report" ]] || { echo "ZAP report 欠落: $report" >&2; exit 1; }
  mv "$RESULTS_DIR/zap-wrk/$report" "$RESULTS_DIR/$report"
done
python3 ../api-fsharp/scripts/zap-to-sarif.py \
  "$RESULTS_DIR/zap-report.json" "$SARIF_DIR/zap.sarif"

echo "=== Schemathesis ==="
set +e
docker run --rm --network host \
  --user "$(id -u):$(id -g)" -e HOME=/tmp \
  -v "$PWD/../api-fsharp/openapi.yaml:/app/openapi.yaml:ro" \
  -v "$PWD/../api-fsharp/schemathesis-hooks.py:/app/schemathesis-hooks.py:ro" \
  -v "$RESULTS_DIR:/app/ci-results:rw" \
  -e SCHEMATHESIS_HOOKS=/app/schemathesis-hooks.py -w /tmp \
  "$SCHEMATHESIS_IMAGE" run /app/openapi.yaml \
  --url "http://localhost:$SPRING_API_PORT" --checks all -n 200 --seed 42 \
  --request-timeout 2.0 --workers 1 --suppress-health-check all \
  --report junit --report-dir /app/ci-results \
  --report-junit-path /app/ci-results/schemathesis-junit.xml
SCHEMATHESIS_EXIT=$?
set -e
[[ "$SCHEMATHESIS_EXIT" -eq 0 ]] || {
  echo "Schemathesis 契約違反: exit=$SCHEMATHESIS_EXIT" >&2
  exit 1
}
python3 scripts/report-to-sarif.py \
  --tool Schemathesis --kind junit \
  --input "$RESULTS_DIR/schemathesis-junit.xml" \
  --output "$SARIF_DIR/schemathesis.sarif"

echo "=== frontend E2E (Spring 接続) ==="
(
  cd ../frontend
  PLAYWRIGHT_JUNIT_OUTPUT_NAME="$RESULTS_DIR/e2e-junit.xml" \
    CI=true E2E_BACKEND=1 E2E_SPRING=1 \
    pnpm exec playwright test --reporter=junit
)
python3 scripts/report-to-sarif.py \
  --tool 'Spring frontend E2E' --kind junit \
  --input "$RESULTS_DIR/e2e-junit.xml" \
  --output "$SARIF_DIR/e2e.sarif"

echo "=== Spring nightly 検査完了 (CodeQL と SARIF audit は workflow が続行) ==="
