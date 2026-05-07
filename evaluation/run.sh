#!/usr/bin/env bash
# 評価ランナー
#
# 使い方:
#   bash evaluation/run.sh                  # 全ケース
#   bash evaluation/run.sh case-001-...     # 単一ケース
#
# 各ケース直下に generated.fs を置いてから実行する。
# generated.fs は AI に prompt.md を読ませて作成すること（手動 or エージェント）。
set -euo pipefail

cd "$(dirname "$0")"

cases=()
if [[ $# -eq 0 ]]; then
    for d in case-*/; do
        cases+=("${d%/}")
    done
else
    cases=("$@")
fi

failed=0
for case_name in "${cases[@]}"; do
    case_dir="$case_name"
    if [[ ! -d "$case_dir" ]]; then
        echo "[SKIP] $case_name: ディレクトリが存在しない"
        failed=$((failed + 1))
        continue
    fi

    echo "=== $case_name ==="

    if [[ ! -f "$case_dir/input.dsl" ]]; then
        echo "  [FAIL] input.dsl がない"
        failed=$((failed + 1))
        continue
    fi

    if [[ ! -f "$case_dir/expected.fs" ]]; then
        echo "  [FAIL] expected.fs がない"
        failed=$((failed + 1))
        continue
    fi

    # input.dsl がパース可能か確認
    if ! (cd ../tools/dsl-parser && uv run dsl-parser "../../evaluation/$case_dir/input.dsl" > /dev/null 2>&1); then
        echo "  [FAIL] input.dsl がパースできない"
        failed=$((failed + 1))
        continue
    fi
    echo "  [OK] input.dsl パース成功"

    if [[ ! -f "$case_dir/generated.fs" ]]; then
        echo "  [PENDING] generated.fs がない（prompt.md を AI に渡して生成してください）"
        continue
    fi

    # expected.fs と generated.fs を diff
    if diff -u "$case_dir/expected.fs" "$case_dir/generated.fs" > /tmp/eval-diff.txt; then
        echo "  [OK] generated.fs == expected.fs (完全一致)"
    else
        added=$(grep -c '^+' /tmp/eval-diff.txt || true)
        removed=$(grep -c '^-' /tmp/eval-diff.txt || true)
        echo "  [DIFF] +$added -$removed 行"
        echo "    詳細: diff -u $case_dir/expected.fs $case_dir/generated.fs"
    fi
done

if [[ $failed -gt 0 ]]; then
    exit 1
fi
