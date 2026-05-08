#!/usr/bin/env bash
# 評価ランナー
#
# 使い方:
#   bash evaluation/run.sh                  # 全ケース・全ターゲット
#   bash evaluation/run.sh case-001-...     # 単一ケース・全ターゲット
#
# 各ケース直下のターゲット規約:
#   meta.md                    評価目的・メソッド・AI に渡す入力・合格基準
#   input.dsl                  共通入力
#   expected.<ext>             ゴールド標準（fs / mmd / als / tla 等、任意）
#   prompt-<target>.md         AI への指示書
#   generated.<ext>            AI が生成した結果（gitignore 対象）
set -euo pipefail

cd "$(dirname "$0")"

# --- normalize.py のセルフテスト（軽量、毎回実行）----
if ! python3 test_normalize.py > /dev/null 2>&1; then
    echo "[FAIL] normalize.py のセルフテスト失敗" >&2
    python3 test_normalize.py
    exit 1
fi

# --- compile-check helper（F# のみ。dotnet があれば実行、無ければ SKIP）----
compile_check_fs() {
    local fs_file="$1"
    if ! command -v dotnet >/dev/null 2>&1; then
        echo "  [SKIP] compile-check (fs): dotnet が見つからない"
        return 0
    fi

    local tmpdir
    tmpdir=$(mktemp -d)
    cat > "$tmpdir/check.fsproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Library</OutputType>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="code.fs" />
  </ItemGroup>
</Project>
EOF
    cp "$fs_file" "$tmpdir/code.fs"

    if (cd "$tmpdir" && dotnet build --nologo --verbosity quiet > /tmp/compile-check.log 2>&1); then
        echo "  [OK] compile-check (fs): $fs_file"
        rm -rf "$tmpdir"
        return 0
    else
        echo "  [FAIL] compile-check (fs): $fs_file"
        echo "    log: /tmp/compile-check.log"
        rm -rf "$tmpdir"
        return 1
    fi
}

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

    # --- sample-diff: expected.<ext> を列挙してターゲットごとに diff ---
    shopt -s nullglob
    expected_files=("$case_dir"/expected.*)
    shopt -u nullglob

    if [[ ${#expected_files[@]} -eq 0 ]]; then
        echo "  [INFO] sample-diff: expected.<ext> がない（このケースは sample-diff を使わない）"
    else
        for exp in "${expected_files[@]}"; do
            ext="${exp##*.}"
            gen="$case_dir/generated.$ext"
            target_label="$ext"

            if [[ ! -f "$gen" ]]; then
                echo "  [PENDING] sample-diff ($target_label): generated.$ext がない (prompt-*.md を AI に渡して生成)"
                continue
            fi

            if cmp -s "$exp" "$gen"; then
                echo "  [OK] sample-diff ($target_label): 完全一致"
            else
                # 意味論比較（コメント・空行・末尾空白を除去）
                exp_norm=$(python3 normalize.py "$ext" "$exp" 2>/dev/null || cat "$exp")
                gen_norm=$(python3 normalize.py "$ext" "$gen" 2>/dev/null || cat "$gen")
                if [[ "$exp_norm" == "$gen_norm" ]]; then
                    echo "  [OK] sample-diff ($target_label): 意味論的に等価（コメント・空行差のみ）"
                else
                    diff -u <(echo "$exp_norm") <(echo "$gen_norm") > /tmp/eval-diff.txt 2>&1 || true
                    added=$(grep -c '^+[^+]' /tmp/eval-diff.txt || true)
                    removed=$(grep -c '^-[^-]' /tmp/eval-diff.txt || true)
                    echo "  [DIFF] sample-diff ($target_label): +$added -$removed 行（意味論的差分）"
                    echo "    詳細: diff -u <(python3 evaluation/normalize.py $ext $exp) <(python3 evaluation/normalize.py $ext $gen)"
                fi
            fi
        done
    fi

    # --- compile-check: F# のみ ---
    if [[ -f "$case_dir/generated.fs" ]]; then
        compile_check_fs "$case_dir/generated.fs" || failed=$((failed + 1))
    fi
done

if [[ $failed -gt 0 ]]; then
    exit 1
fi
