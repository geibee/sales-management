#!/usr/bin/env bash
# 評価ランナー
#
# 使い方:
#   bash evaluation/run.sh                  # 全ケース・全ターゲット
#   bash evaluation/run.sh case-001-...     # 単一ケース・全ターゲット
#
# 各ケース直下のターゲット規約:
#   input.dsl                  共通入力
#   expected.<ext>             ゴールド標準（fs / mmd / als / tla 等）
#   prompt-<target>.md         AI への指示書
#   generated.<ext>            AI が生成した結果（gitignore 対象）
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

    if [[ ! -f "$case_dir/meta.md" ]]; then
        echo "  [FAIL] meta.md がない（評価目的が未定義）"
        failed=$((failed + 1))
        continue
    fi

    if [[ ! -f "$case_dir/input.dsl" ]]; then
        echo "  [FAIL] input.dsl がない"
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

    # ターゲットごと（expected.<ext> を列挙）
    shopt -s nullglob
    expected_files=("$case_dir"/expected.*)
    shopt -u nullglob

    if [[ ${#expected_files[@]} -eq 0 ]]; then
        echo "  [WARN] expected.<ext> がない"
        continue
    fi

    for exp in "${expected_files[@]}"; do
        ext="${exp##*.}"
        gen="$case_dir/generated.$ext"
        target_label="$ext"

        if [[ ! -f "$gen" ]]; then
            echo "  [PENDING] $target_label: generated.$ext がない (prompt-*.md を AI に渡して生成)"
            continue
        fi

        if diff -u "$exp" "$gen" > /tmp/eval-diff.txt 2>&1; then
            echo "  [OK] $target_label: 完全一致"
        else
            added=$(grep -c '^+[^+]' /tmp/eval-diff.txt || true)
            removed=$(grep -c '^-[^-]' /tmp/eval-diff.txt || true)
            echo "  [DIFF] $target_label: +$added -$removed 行"
            echo "    詳細: diff -u $exp $gen"
        fi
    done
done

if [[ $failed -gt 0 ]]; then
    exit 1
fi
