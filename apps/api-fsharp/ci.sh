#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"

if [[ "${QUALITY_HEAVY_CHILD:-0}" != 1 ]]; then
    exec bash ../../scripts/full-verify.sh fsharp
fi
RESULTS_DIR="${QUALITY_RUN_DIR:?全量検証の run directory が必要です}"
SARIF_DIR="$RESULTS_DIR/sarif"
mkdir -p "$SARIF_DIR"
APP_PID=""
finish() {
    local original=$?
    trap - EXIT
    if [[ -n "$APP_PID" ]]; then
        kill "$APP_PID" 2>/dev/null || true
        wait "$APP_PID" 2>/dev/null || true
    fi
    # 失敗時にも生成済みネイティブレポートを保存・変換する。
    if [[ -f "$RESULTS_DIR/zap-report.json" ]]; then
        python3 scripts/zap-to-sarif.py "$RESULTS_DIR/zap-report.json" "$SARIF_DIR/zap.sarif" || original=1
    fi
    if [[ -f "$RESULTS_DIR/schemathesis-junit.xml" ]]; then
        python3 scripts/junit-to-sarif.py "$RESULTS_DIR/schemathesis-junit.xml" "$SARIF_DIR/schemathesis.sarif" Schemathesis || original=1
    fi
    exit "$original"
}
trap finish EXIT
for tool in dotnet python3 docker curl gitleaks trivy pnpm; do
    command -v "$tool" >/dev/null || { echo "必須 tool 欠落: $tool" >&2; exit 1; }
done

echo "=== Pact Broker ヘルスチェック ==="
PACT_BROKER_URL_DEFAULT="http://localhost:9292"
PACT_BROKER_URL="${PACT_BROKER_URL:-$PACT_BROKER_URL_DEFAULT}"
PACT_ENABLED=1
curl -fsu pact:pact --max-time 3 "$PACT_BROKER_URL/diagnostic/status/heartbeat" >/dev/null
PACT_BROKER_URL="$PACT_BROKER_URL" bash scripts/pact-publish.sh

echo "=== シークレット検出 (SARIF) ==="
( cd ../.. && gitleaks detect --source . --redact \
    --report-format sarif \
    --report-path "$SARIF_DIR/gitleaks.sarif" )

echo "=== パッケージ脆弱性スキャン (SARIF) ==="
trivy fs --scanners vuln,secret,misconfig --severity HIGH,CRITICAL --exit-code 1 \
    --format sarif --output "$SARIF_DIR/trivy.sarif" .

echo "=== SBOM 生成 (CycloneDX) ==="
dotnet CycloneDX src/SalesManagement/SalesManagement.fsproj \
    --output-format Json \
    --output "$RESULTS_DIR" \
    --filename sbom-fsharp.cdx.json
python3 ../api-spring/scripts/report-to-sarif.py --tool CycloneDX --kind artifact \
    --input "$RESULTS_DIR/sbom-fsharp.cdx.json" --output "$SARIF_DIR/cyclonedx.sarif"

ZAP_ENABLED="${ZAP_ENABLED:-1}"
SCHEMATHESIS_ENABLED="${SCHEMATHESIS_ENABLED:-1}"
NEED_APP=0
[ "$ZAP_ENABLED" = "1" ] && NEED_APP=1
[ "$SCHEMATHESIS_ENABLED" = "1" ] && NEED_APP=1
[ $PACT_ENABLED -eq 1 ] && NEED_APP=1

if [ $NEED_APP -eq 1 ]; then
    echo "=== アプリ起動 (Pact / ZAP / Schemathesis 用) ==="
    # fuzz は大量リクエストを送るため、レート制限を実質無効化する
    # (429 はフレークの温床で、error 昇格した Schemathesis ゲートを不安定にする)
    dotnet run --project src/SalesManagement --no-build -- --RateLimit:PermitLimit=1000000 &
    APP_PID=$!

    for _ in {1..30}; do
        if curl -sf --max-time 3 http://localhost:5000/health >/dev/null 2>&1; then
            break
        fi
        sleep 1
    done

    if [ $PACT_ENABLED -eq 1 ]; then
        echo "=== Provider 検証 (Pact) ==="
        python3 ../../scripts/quality-run.py step --directory "$RESULTS_DIR" --id pact --tool 'Pact provider' \
            --test-report "$RESULTS_DIR/pact-results/pact.trx" --minimum-test-cases 1 -- \
            env PACT_BROKER_URL="$PACT_BROKER_URL" \
            dotnet test tests/SalesManagement.Tests --filter "Category=Pact" --no-build \
            --logger:"trx;LogFileName=pact.trx" --results-directory "$RESULTS_DIR/pact-results"
    fi
fi

if [ "$ZAP_ENABLED" = "1" ]; then
    echo "=== DAST (OWASP ZAP) ==="
    set +e
    ZAP_WRK="$RESULTS_DIR/zap-wrk"
    ZAP_IMAGE="ghcr.io/zaproxy/zaproxy:2.17.0@sha256:8d387b1a63e3425beef4846e39719f5af2a787753af2d8b6558c6257d7a577a2"
    mkdir -p "$RESULTS_DIR/zap-wrk"
    chmod 0777 "$RESULTS_DIR/zap-wrk"
    rm -f "$RESULTS_DIR/zap-report.json" "$RESULTS_DIR/zap-report.html" \
        "$RESULTS_DIR/zap-report.md" "$RESULTS_DIR/zap.out" \
        "$SARIF_DIR/zap.sarif" \
        "$ZAP_WRK/zap-report.json" "$ZAP_WRK/zap-report.html" \
        "$ZAP_WRK/zap-report.md"
    # zap-api-scan.py は -c で渡す config を /zap/wrk/ から読むので、
    # ZAP 専用の書込領域にコピーしてから渡す
    cp zap-rules.tsv "$ZAP_WRK/zap-rules.tsv"
    # `-addonuninstall domxss`: DOM XSS addon を起動時に削除する。
    #   - 当 API は JSON のみを返すバックエンド (application/json / problem+json)。
    #     DOM XSS はブラウザが HTML をレンダーする層の問題で、JSON エンドポイント
    #     には構造的に該当しない (フロントは別 repo の React SPA で別途検査)
    #   - addon は headless Firefox + geckodriver を起動するが、aarch64 WSL2 では
    #     marionette ポート読込失敗で 4 連続ハング → スキャン全体を巻き込んで exit=3
    #   - addon ごと外すことで Selenium 経路を完全に避ける
    # イメージ既定の zap ユーザーで動かす。ホスト UID を --user で注入すると、
    # ZAP のホームにある addon / 設定を読めず起動に失敗するため変更しない。
    # コンテナへ書込許可するのは専用の zap-wrk のみ。
    docker run --rm --network host \
        -v "$PWD/openapi.yaml:/zap/openapi.yaml:ro" \
        -v "$RESULTS_DIR/zap-wrk:/zap/wrk:rw" \
        -w /zap/wrk \
        "$ZAP_IMAGE" \
        zap-api-scan.py \
            -t /zap/openapi.yaml \
            -f openapi \
            -r zap-report.html \
            -w zap-report.md \
            -J zap-report.json \
            -c zap-rules.tsv \
            -z "-config api.disablekey=true -addonuninstall domxss" \
            -T 5 \
            -l WARN 2>&1 | tee "$RESULTS_DIR/zap.out"
    ZAP_EXIT=${PIPESTATUS[0]}
    for report in zap-report.json zap-report.html zap-report.md; do
        if [ -f "$ZAP_WRK/$report" ]; then
            mv "$ZAP_WRK/$report" "$RESULTS_DIR/$report"
        fi
    done
    set -e
else
    echo "=== DAST (OWASP ZAP) — SKIPPED (ZAP_ENABLED=0) ==="
    echo "  高速モード。最終検証時は ZAP_ENABLED=1 ./ci.sh を実行してください"
    ZAP_EXIT=0
    # 古い zap.sarif が SARIF マージに混ざらないように削除
    rm -f "$RESULTS_DIR/sarif/zap.sarif" "$RESULTS_DIR/zap-report.json" "$RESULTS_DIR/zap-report.html" "$RESULTS_DIR/zap-report.md"
    echo "ZAP_ENABLED=0 のためスキップ" > "$RESULTS_DIR/zap.out"
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
    # --user host UID:GID で root 所有を避ける (host の ci-results 直下に書く)。
    # 作業ディレクトリは /tmp とし、実行時キャッシュ .schemathesis を mount 先へ
    # 作ろうとして権限エラーになるのを防ぐ。
    docker run --rm --network host \
        --user "$(id -u):$(id -g)" \
        -e HOME=/tmp \
        -v "$PWD/openapi.yaml:/app/openapi.yaml:ro" \
        -v "$PWD/schemathesis-hooks.py:/app/schemathesis-hooks.py:ro" \
        -v "$RESULTS_DIR:/app/ci-results:rw" \
        -e SCHEMATHESIS_HOOKS=/app/schemathesis-hooks.py \
        -w /tmp \
        schemathesis/schemathesis:4.24.3@sha256:dd1ebf7519958c34c276a65c20f9f2f808dbefb06c86163eb284ff5674c6a9f3 \
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
    echo "Schemathesis exit=$SCHEMATHESIS_EXIT"
else
    echo "=== API Fuzz (Schemathesis) — SKIPPED (SCHEMATHESIS_ENABLED=0) ==="
    SCHEMATHESIS_EXIT=0
    rm -f "$RESULTS_DIR/sarif/schemathesis.sarif" "$RESULTS_DIR/schemathesis-junit.xml" "$RESULTS_DIR/schemathesis.tar.gz"
fi

if [ "$ZAP_EXIT" -ne 0 ] && [ "$ZAP_EXIT" -ne 2 ]; then
    case "$ZAP_EXIT" in
        1) echo "DAST: FAIL 判定の脆弱性が検出されました (exit=1)" ;;
        3) echo "DAST: ZAP の実行異常です (exit=3)" ;;
        *) echo "DAST: ZAP の予期しない終了コードです (exit=$ZAP_EXIT)" ;;
    esac
    echo "実行ログ: $RESULTS_DIR/zap.out"
    exit 1
fi
if [ "$ZAP_EXIT" -eq 2 ]; then
    echo "DAST: WARN を記録しました (exit=2、ゲート成功)"
fi

# Schemathesis は既知検出のトリアージ完了に伴い error 昇格 (issue #9 Tier2-15)。
# unsupported_method (405 全数対応) は HTTP セマンティクス層の導入 (issue #15 §1,
# Api/HttpSemantics.fs) に伴い有効化済み。
# 認可系チェックは hooks で root security を外しているため対象外
# (認可の全数検証は決定的な AuthorizationMatrixTests が担う)
if [ "$SCHEMATHESIS_ENABLED" = "1" ] && [ "$SCHEMATHESIS_EXIT" -ne 0 ]; then
    echo "API Fuzz: Schemathesis が契約違反を検出しました (exit=$SCHEMATHESIS_EXIT)"
    exit 1
fi

kill "$APP_PID" 2>/dev/null || true
wait "$APP_PID" 2>/dev/null || true
APP_PID=""
echo "=== 実 API E2E ==="
(
    cd ../frontend
    PLAYWRIGHT_JUNIT_OUTPUT_NAME="$RESULTS_DIR/e2e-junit.xml" CI=true E2E_BACKEND=1 \
        pnpm exec playwright test --reporter=junit
)
python3 ../api-spring/scripts/report-to-sarif.py --tool 'Backend E2E' --kind junit \
    --input "$RESULTS_DIR/e2e-junit.xml" --output "$SARIF_DIR/e2e.sarif"
