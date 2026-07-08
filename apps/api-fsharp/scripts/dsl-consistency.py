#!/usr/bin/env python3
"""DSL ↔ コード整合ゲート — behavior の実装漏れを検出する (issue #9 §3)。

dsl/domain-model.md の各 `behavior` 行には `// fn: <Module>.<関数名>` の注釈を
付ける規約とし、本スクリプトが以下を決定的 (grep レベル) に突合する:

  1. 全 behavior 行に `// fn:` 注釈があること
     (DSL に behavior を足したのに実装マッピングが無い = 実装漏れの疑い → 失敗)
  2. 注釈先の <Module>.fs が src/SalesManagement/ 配下に実在すること
  3. そのファイルに `let [private] [rec] <関数名>` の定義が実在すること
     (実装が消えた / リネームされたのに DSL が追随していない → 失敗)

専用パーサーは使わない (dsl/README.md の方針)。behavior 行の書式が崩れて
列挙が 0 件になった場合も fail-closed で失敗させる。

使い方: python3 scripts/dsl-consistency.py  (apps/api-fsharp から実行)
"""

import re
import sys
from pathlib import Path

script_dir = Path(__file__).resolve().parent
repo_root = script_dir.parents[2]
dsl_path = repo_root / "dsl" / "domain-model.md"
src_root = script_dir.parent / "src" / "SalesManagement"

BEHAVIOR_RE = re.compile(r"^behavior\s+(\S+)\s*=")
FN_ANNOTATION_RE = re.compile(r"//\s*fn:\s*([A-Za-z]\w*)\.([A-Za-z]\w*)\s*$")


def let_defined(module_file: Path, fn: str) -> bool:
    pattern = re.compile(rf"^\s*let\s+(private\s+)?(rec\s+)?{re.escape(fn)}\b", re.M)
    return bool(pattern.search(module_file.read_text(encoding="utf-8")))


def main() -> int:
    if not dsl_path.is_file():
        print(f"[dsl-consistency] FAIL: {dsl_path} がありません (fail-closed)", file=sys.stderr)
        return 1

    errors: list[str] = []
    behaviors: list[tuple[str, str, str]] = []

    for line in dsl_path.read_text(encoding="utf-8").splitlines():
        m = BEHAVIOR_RE.match(line)
        if not m:
            continue
        name = m.group(1)
        fn_match = FN_ANNOTATION_RE.search(line)
        if not fn_match:
            errors.append(f"behavior '{name}' に `// fn: <Module>.<関数名>` 注釈がありません")
            continue
        behaviors.append((name, fn_match.group(1), fn_match.group(2)))

    if not behaviors and not errors:
        print(
            "[dsl-consistency] FAIL: behavior を 1 件も列挙できません "
            "(書式が変わった場合はスクリプトを追随させること。fail-closed)",
            file=sys.stderr,
        )
        return 1

    # モジュール名 → ファイルの解決は一度だけ (src 配下を再帰探索)
    module_files = {p.stem: p for p in src_root.rglob("*.fs")}

    for name, module, fn in behaviors:
        module_file = module_files.get(module)
        if module_file is None:
            errors.append(f"behavior '{name}' の注釈先モジュール {module}.fs が src/SalesManagement/ にありません")
            continue
        if not let_defined(module_file, fn):
            errors.append(f"behavior '{name}' の注釈先 {module}.{fn} が定義されていません")

    if errors:
        print(
            "[dsl-consistency] FAIL: DSL と実装の不整合 (" + str(len(errors)) + " 件):\n  " + "\n  ".join(errors),
            file=sys.stderr,
        )
        return 1

    print(f"[dsl-consistency] OK: {len(behaviors)} behavior すべてに実装が存在します")
    return 0


if __name__ == "__main__":
    sys.exit(main())
