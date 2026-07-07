#!/usr/bin/env python3
"""契約カバレッジラチェット — 「テスト未到達の operation」の増加を検出するゲート。

統合テスト実行中に Support/OpenApiValidation.fs が 2xx 到達した operationId を
coverage/operation-coverage.json へ記録する。本スクリプトは openapi.yaml の
全 operationId との差分 (未到達一覧) を operation-coverage-baseline.json と比較する:

  - baseline に無い operation が未到達 → 失敗
    (API を追加したのに統合テストが 1 本も 2xx で到達していない = テスト追加の強制)
  - 未到達が baseline より減った → 合格。RATCHET_UPDATE=1 で baseline を縮めて
    コミットすれば、以後はその集合が新しい上限になる

使い方: python3 scripts/operation-coverage-ratchet.py <operation-coverage.json>
"""

import json
import os
import re
import sys
from pathlib import Path

script_dir = Path(__file__).resolve().parent
spec_path = script_dir.parent / "openapi.yaml"
baseline_path = script_dir.parent / "operation-coverage-baseline.json"


def spec_operation_ids() -> set[str]:
    # 本リポジトリの openapi.yaml は operationId を 1 行 1 定義で書く規約のため、
    # 行単位の抽出で決定的に列挙できる (YAML パーサ非依存)
    ids = set(re.findall(r"^\s*operationId:\s*(\w+)\s*$", spec_path.read_text(encoding="utf-8"), re.M))
    if len(ids) < 10:
        print(f"[operation-coverage] FAIL: openapi.yaml から operationId を列挙できません ({len(ids)} 件)", file=sys.stderr)
        sys.exit(1)  # fail-closed: 列挙が壊れたまま緑にしない
    return ids


def main() -> int:
    if len(sys.argv) != 2:
        print("usage: operation-coverage-ratchet.py <operation-coverage.json>", file=sys.stderr)
        return 2

    recorded_path = Path(sys.argv[1])
    if not recorded_path.is_file():
        print(f"[operation-coverage] FAIL: 記録ファイルがありません: {recorded_path}", file=sys.stderr)
        return 1  # fail-closed

    recorded = set(json.loads(recorded_path.read_text(encoding="utf-8")))
    baseline = set(json.loads(baseline_path.read_text(encoding="utf-8")))
    uncovered = spec_operation_ids() - recorded

    newly_uncovered = sorted(uncovered - baseline)
    if newly_uncovered:
        print(
            "[operation-coverage] FAIL: 統合テストが 2xx で到達していない operation が増えています:\n  "
            + "\n  ".join(newly_uncovered)
            + "\n該当 operation の統合テストを追加してください (Support/ApiFixture 経由の 2xx 到達で記録されます)。",
            file=sys.stderr,
        )
        return 1

    improved = sorted(baseline - uncovered)
    print(f"[operation-coverage] OK: 到達 {len(recorded)} 件 / 未到達 {len(uncovered)} 件 (baseline {len(baseline)} 件)")
    if uncovered:
        print("[operation-coverage] 未到達 (baseline 内): " + ", ".join(sorted(uncovered)))
    if improved:
        if os.environ.get("RATCHET_UPDATE") == "1":
            baseline_path.write_text(json.dumps(sorted(uncovered), indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
            print("[operation-coverage] baseline を縮小しました。operation-coverage-baseline.json をコミットしてください。")
        else:
            print(f"[operation-coverage] baseline より {len(improved)} 件改善。RATCHET_UPDATE=1 で縮小できます。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
