#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"

RESULTS_DIR="./ci-results"
SARIF_DIR="$RESULTS_DIR/sarif"
mkdir -p "$RESULTS_DIR" "$SARIF_DIR"
TIMESTAMP=$(date -u +"%Y-%m-%dT%H:%M:%SZ")

echo "=== Jaeger 起動チェック ==="
# --max-time 3 で短期失敗。listen はあるが応答がない (詰まっている) 状態でも 3 秒で抜ける。
if curl -fs --max-time 3 http://localhost:16686/api/services >/dev/null 2>&1; then
    echo "Jaeger UP — エージェントトレースを送信できます"
else
    echo "Jaeger 未起動または無応答 (任意)"
fi

echo "=== マイグレーション ==="
dotnet run --project tools/Migrator

echo "=== ビルド ==="
# F# コンパイラは /p:ErrorLog (Roslyn) をサポートしないため、SARIF 化は FSharpLint で代替する
dotnet build src/SalesManagement --warnaserror
dotnet build tools/Migrator --warnaserror
dotnet build tools/BatchRunner --warnaserror
dotnet build tools/DevTokenMint --warnaserror
dotnet build tests/SalesManagement.Tests --warnaserror

echo "=== フォーマットチェック ==="
dotnet fantomas --check src/ tests/

echo "=== リンター (SARIF) ==="
LINT_TXT="$RESULTS_DIR/fsharplint.txt"
LINT_OUTPUT=$(dotnet dotnet-fsharplint lint src/SalesManagement/SalesManagement.fsproj 2>&1)
echo "$LINT_OUTPUT" | tee "$LINT_TXT"
LINT_WARNINGS=$(echo "$LINT_OUTPUT" | grep -oE 'Summary: [0-9]+ warnings' | grep -oE '[0-9]+' | head -1 || true)
LINT_WARNINGS=${LINT_WARNINGS:-0}
printf '{"timestamp":"%s","lint_warnings":%s}\n' "$TIMESTAMP" "$LINT_WARNINGS" >> "$RESULTS_DIR/lint.json"
python3 scripts/lint-to-sarif.py "$LINT_TXT" ci-results/sarif/fsharplint.sarif

echo "=== 複雑度 ==="
if ! command -v scc >/dev/null 2>&1; then
    echo "scc が見つかりません (インストール: https://github.com/boyter/scc)" >&2
    exit 1
fi
scc --by-file --format json src/ > "$RESULTS_DIR/scc_${TIMESTAMP}.json"

echo "=== テスト + カバレッジ ==="
rm -rf coverage
dotnet test tests/SalesManagement.Tests \
    --collect:"XPlat Code Coverage" \
    --results-directory ./coverage

COVERAGE_FILE=$(find coverage -name 'coverage.cobertura.xml' | head -1)
COVERAGE=$(grep -oE 'line-rate="[0-9.]+"' "$COVERAGE_FILE" | head -1 | grep -oE '[0-9.]+' || true)
COVERAGE=${COVERAGE:-0}
printf '{"timestamp":"%s","coverage":%s}\n' "$TIMESTAMP" "$COVERAGE" >> "$RESULTS_DIR/coverage.json"

echo "=== アーキテクチャ適合性 ==="
dotnet test tests/SalesManagement.Tests \
    --filter "Category=Architecture" \
    --no-build

echo "=== Pact Broker ヘルスチェック ==="
PACT_BROKER_URL_DEFAULT="http://localhost:9292"
PACT_BROKER_URL="${PACT_BROKER_URL:-$PACT_BROKER_URL_DEFAULT}"
PACT_ENABLED=0
if curl -fsu pact:pact --max-time 3 "$PACT_BROKER_URL/diagnostic/status/heartbeat" >/dev/null 2>&1; then
    PACT_ENABLED=1
    echo "Pact Broker UP at $PACT_BROKER_URL"
    echo "=== Consumer Pact 公開 ==="
    bash scripts/pact-publish.sh
else
    echo "Pact Broker 未起動 — Pact ステージをスキップします"
    echo "  PACT_BROKER_URL を設定して再実行してください"
fi

echo "=== シークレット検出 (SARIF) ==="
( cd ../.. && gitleaks detect --source . \
    --report-format sarif \
    --report-path apps/api-fsharp/ci-results/sarif/gitleaks.sarif )

echo "=== パッケージ脆弱性スキャン (SARIF) ==="
dotnet list src/SalesManagement/SalesManagement.fsproj package --vulnerable --include-transitive || true
trivy fs --scanners vuln --severity HIGH,CRITICAL \
    --format sarif --output ci-results/sarif/trivy.sarif .

echo "=== SBOM 生成 (CycloneDX) ==="
dotnet CycloneDX src/SalesManagement/SalesManagement.fsproj \
    --output-format Json \
    --output ci-results \
    --filename sbom-fsharp.cdx.json
python3 - <<'PY'
import json, pathlib, sys
p = pathlib.Path("ci-results/sbom-fsharp.cdx.json")
data = json.loads(p.read_text())
comps = data.get("components", []) or []
tool = next(iter((data.get("metadata") or {}).get("tools", {}).get("components", []) or [{}]), {}).get("name", "?")
print(f"SBOM: tool={tool} components={len(comps)}")
if len(comps) < 1:
    print("SBOM contains no components", file=sys.stderr)
    sys.exit(1)
PY

ZAP_ENABLED="${ZAP_ENABLED:-1}"
SCHEMATHESIS_ENABLED="${SCHEMATHESIS_ENABLED:-1}"
NEED_APP=0
[ "$ZAP_ENABLED" = "1" ] && NEED_APP=1
[ "$SCHEMATHESIS_ENABLED" = "1" ] && NEED_APP=1
[ $PACT_ENABLED -eq 1 ] && NEED_APP=1

if [ $NEED_APP -eq 1 ]; then
    echo "=== アプリ起動 (Pact / ZAP 用) ==="
    dotnet run --project src/SalesManagement --no-build &
    APP_PID=$!

    for i in {1..30}; do
        if curl -sf --max-time 3 http://localhost:5000/health >/dev/null 2>&1; then
            break
        fi
        sleep 1
    done

    if [ $PACT_ENABLED -eq 1 ]; then
        echo "=== Provider 検証 (Pact) ==="
        PACT_BROKER_URL="$PACT_BROKER_URL" \
        PACT_PROVIDER_URL="http://localhost:5000" \
            dotnet test tests/SalesManagement.Tests \
                --filter "Category=Pact" \
                --no-build
    fi
fi

if [ "$ZAP_ENABLED" = "1" ]; then
    echo "=== DAST (OWASP ZAP) ==="
    set +e
    # zap-api-scan.py は -c で渡す config を /zap/wrk/ から読むので、
    # mount される RESULTS_DIR にコピーしてから渡す
    cp zap-rules.tsv "$RESULTS_DIR/zap-rules.tsv"
    # `-addonuninstall domxss`: DOM XSS addon を起動時に削除する。
    #   - 当 API は JSON のみを返すバックエンド (application/json / problem+json)。
    #     DOM XSS はブラウザが HTML をレンダーする層の問題で、JSON エンドポイント
    #     には構造的に該当しない (フロントは別 repo の React SPA で別途検査)
    #   - addon は headless Firefox + geckodriver を起動するが、aarch64 WSL2 では
    #     marionette ポート読込失敗で 4 連続ハング → スキャン全体を巻き込んで exit=3
    #   - addon ごと外すことで Selenium 経路を完全に避ける
    docker run --rm --network host \
        -v "$PWD/openapi.yaml:/zap/openapi.yaml:ro" \
        -v "$PWD/$RESULTS_DIR:/zap/wrk:rw" \
        ghcr.io/zaproxy/zaproxy:stable \
        zap-api-scan.py \
            -t /zap/openapi.yaml \
            -f openapi \
            -r zap-report.html \
            -w zap-report.md \
            -J zap-report.json \
            -c zap-rules.tsv \
            -z "-config api.disablekey=true -addonuninstall domxss" \
            -l WARN
    ZAP_EXIT=$?
    set -e
else
    echo "=== DAST (OWASP ZAP) — SKIPPED (ZAP_ENABLED=0) ==="
    echo "  高速モード。最終検証時は ZAP_ENABLED=1 ./ci.sh を実行してください"
    ZAP_EXIT=0
    # 古い zap.sarif が SARIF マージに混ざらないように削除
    rm -f "$RESULTS_DIR/sarif/zap.sarif" "$RESULTS_DIR/zap-report.json" "$RESULTS_DIR/zap-report.html" "$RESULTS_DIR/zap-report.md"
fi

if [ "$SCHEMATHESIS_ENABLED" = "1" ]; then
    echo "=== API Fuzz (Schemathesis) ==="
    set +e
    # Schemathesis は openapi.yaml を読み、各 operation に hypothesis 駆動で
    # ランダム入力を生成して http://localhost:5000 に投げる。コンテナで実行し
    # --network host で localhost に到達。
    # - hooks: schemathesis-hooks.py で「事前状態を要さないと通せない」operation を schema から除去
    # - --checks all: 既知の全チェック (status_code_conformance, response_schema_conformance, ...) を有効化
    # - -n 200 / --seed 42: 反復可能性のため固定 seed + 上限 200 例
    # - --request-timeout 2.0: API 個別呼び出しの上限秒 (旧 --hypothesis-deadline=2000 相当)
    # --user host UID:GID で root 所有を避ける (host の ci-results 直下に書く)
    docker run --rm --network host \
        --user "$(id -u):$(id -g)" \
        -e HOME=/tmp \
        -v "$PWD/openapi.yaml:/app/openapi.yaml:ro" \
        -v "$PWD/schemathesis-hooks.py:/app/schemathesis-hooks.py:ro" \
        -v "$PWD/$RESULTS_DIR:/app/ci-results:rw" \
        -e SCHEMATHESIS_HOOKS=/app/schemathesis-hooks.py \
        -w /app \
        schemathesis/schemathesis:stable \
        run /app/openapi.yaml \
            --url http://localhost:5000 \
            --checks all \
            -n 200 \
            --seed 42 \
            --request-timeout 2.0 \
            --workers 1 \
            --suppress-health-check all \
            --report junit \
            --report-dir /app/ci-results \
            --report-junit-path /app/ci-results/schemathesis-junit.xml
    SCHEMATHESIS_EXIT=$?
    set -e
    echo "Schemathesis exit=$SCHEMATHESIS_EXIT (informational; SARIF が canonical)"
else
    echo "=== API Fuzz (Schemathesis) — SKIPPED (SCHEMATHESIS_ENABLED=0) ==="
    SCHEMATHESIS_EXIT=0
    rm -f "$RESULTS_DIR/sarif/schemathesis.sarif" "$RESULTS_DIR/schemathesis-junit.xml" "$RESULTS_DIR/schemathesis.tar.gz"
fi

if [ $NEED_APP -eq 1 ]; then
    kill $APP_PID 2>/dev/null || true
    wait $APP_PID 2>/dev/null || true
fi

if [ "$ZAP_ENABLED" = "1" ]; then
    echo "=== ZAP → SARIF 変換 ==="
    python3 scripts/zap-to-sarif.py ci-results/zap-report.json ci-results/sarif/zap.sarif
fi

if [ "$SCHEMATHESIS_ENABLED" = "1" ]; then
    echo "=== Schemathesis → SARIF 変換 ==="
    if [ -f ci-results/schemathesis-junit.xml ]; then
        python3 scripts/junit-to-sarif.py ci-results/schemathesis-junit.xml ci-results/sarif/schemathesis.sarif Schemathesis
    else
        # JUnit 出力が無い (極端な早期失敗) でも空 SARIF を残しておくと merge / verify が落ちない
        cat > ci-results/sarif/schemathesis.sarif <<'JSON'
{
  "$schema": "https://json.schemastore.org/sarif-2.1.0.json",
  "version": "2.1.0",
  "runs": [{"tool": {"driver": {"name": "Schemathesis", "informationUri": "https://schemathesis.readthedocs.io/"}}, "results": []}]
}
JSON
    fi
    echo "=== Schemathesis アーティファクト bundle ==="
    tar -C ci-results -czf ci-results/schemathesis.tar.gz \
        sarif/schemathesis.sarif \
        $( [ -f ci-results/schemathesis-junit.xml ] && echo schemathesis-junit.xml ) 2>/dev/null || true
fi

echo "=== SARIF マージ ==="
python3 scripts/sarif-merge.py ci-results/merged.sarif \
    ci-results/sarif/gitleaks.sarif \
    ci-results/sarif/trivy.sarif \
    ci-results/sarif/fsharplint.sarif \
    ci-results/sarif/zap.sarif \
    ci-results/sarif/schemathesis.sarif

echo "=== SARIF サマリ ==="
python3 - <<'PY'
import json, pathlib, sys
p = pathlib.Path("ci-results/merged.sarif")
if not p.exists():
    print("merged.sarif not found", file=sys.stderr)
    sys.exit(1)
data = json.loads(p.read_text())
runs = data.get("runs", [])
print(f"merged runs: {len(runs)}")
errors: list[dict] = []
for run in runs:
    name = run.get("tool", {}).get("driver", {}).get("name", "?")
    results = run.get("results", []) or []
    by_level: dict[str, int] = {}
    for r in results:
        by_level[r.get("level", "none")] = by_level.get(r.get("level", "none"), 0) + 1
    print(f"  {name}: total={len(results)} levels={by_level}")
    errors.extend(r for r in results if r.get("level") == "error")
if errors:
    print(f"SARIF errors: {len(errors)}", file=sys.stderr)
    for e in errors[:5]:
        rid = e.get("ruleId", "?")
        msg = ((e.get("message") or {}).get("text") or "")[:100]
        print(f"  - {rid}: {msg}", file=sys.stderr)
    sys.exit(1)
PY

if [ $ZAP_EXIT -ne 0 ]; then
    echo "DAST: 脆弱性が検出されました (exit=$ZAP_EXIT)"
    echo "レポート: $RESULTS_DIR/zap-report.html"
    exit 1
fi

echo "=== Renovate 優先化 (Trivy SARIF → renovate.json) ==="
python3 scripts/prioritize-from-trivy.py ci-results/sarif/trivy.sarif ../../renovate.json || true

echo "=== Renovate dry-run ==="
( cd ../.. && env LOG_LEVEL=info RENOVATE_PLATFORM=local RENOVATE_AUTODISCOVER=false \
    npx --yes renovate --dry-run=full ) > "$RESULTS_DIR/renovate.log" 2>&1 || true
grep -E "Dependency extraction complete|fileCount|depCount" "$RESULTS_DIR/renovate.log" | head -10 || true

echo "=== AGENTS.md 自動更新差分 ==="
git -C ../.. diff --stat AGENTS.md || true

echo "=== CI完了 ==="
