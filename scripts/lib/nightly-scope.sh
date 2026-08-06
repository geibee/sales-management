# shellcheck shell=bash
# NEED_NIGHTLY_HEAVY / NEED_NIGHTLY_E2E は source 元 (nightly.yml / bats) が参照する。
# shellcheck disable=SC2034
#
# nightly の重量ジョブを変更パスから選ぶ純粋関数。呼び出し側は両変数を 0 で
# 初期化し、変更ファイルを改行区切りで標準入力へ渡すこと。

classify_nightly_paths() {
  local path
  while IFS= read -r path; do
    [[ -z "$path" ]] && continue
    case "$path" in
      .github/workflows/nightly.yml)
        NEED_NIGHTLY_HEAVY=1
        NEED_NIGHTLY_E2E=1
        ;;
      apps/api-fsharp/ci.sh|\
      apps/api-fsharp/docker-compose.yml|\
      apps/api-fsharp/openapi.yaml|\
      apps/api-fsharp/zap-rules.tsv|\
      apps/api-fsharp/schemathesis-hooks.py|\
      apps/api-fsharp/scripts/zap-to-sarif.py|\
      apps/api-fsharp/scripts/junit-to-sarif.py|\
      apps/api-fsharp/scripts/sarif-merge.py|\
      apps/api-fsharp/wiremock/*|\
      scripts/lib/nightly-scope.sh|\
      scripts/tests/ci-container-permissions.bats|\
      scripts/tests/nightly-scope.bats|\
      scripts/tests/python/test_sarif_converters.py)
        NEED_NIGHTLY_HEAVY=1
        ;;
      apps/frontend/playwright.config.ts|\
      apps/frontend/package.json|\
      apps/frontend/pnpm-lock.yaml|\
      apps/frontend/tests/e2e/backend-*.spec.ts)
        NEED_NIGHTLY_E2E=1
        ;;
    esac
  done
}
