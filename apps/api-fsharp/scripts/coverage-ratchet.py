#!/usr/bin/env python3
"""カバレッジ・ラチェット (backend) — 「前回値から下げたら失敗」方式のしきい値ゲート。

frontend の scripts/coverage-ratchet.mjs と同じ設計:
  - coverage-baseline.json に記録した実測値を下限として使う
  - 現在値が baseline を EPSILON 以上下回る → 失敗 (テストなしのコード追加を検出)
  - 現在値が baseline を上回る → 合格。RATCHET_UPDATE=1 で baseline を引き上げて
    コミットすれば、以後はその値が新しい下限になる

テスト数ラチェット (BASELINE_TEST_COUNT) は「中身の薄いテストで数だけ稼ぐ」
ゲーミングに弱いため、本ラチェットと併用する (issue #9 Tier1-2)。

入力: coverlet (--collect:"XPlat Code Coverage") が出力する coverage.cobertura.xml
基準: apps/api-fsharp/coverage-baseline.json (コミット済み)

使い方: python3 scripts/coverage-ratchet.py <coverage.cobertura.xml>
"""

import json
import os
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

# 実行順・Testcontainers 起動タイミングで僅かに揺れることがあるため、誤差はここまで許容する (pt)
EPSILON = 0.1
METRICS = ("line", "branch")

baseline_path = Path(__file__).resolve().parent.parent / "coverage-baseline.json"


def main() -> int:
    if len(sys.argv) != 2:
        print("usage: coverage-ratchet.py <coverage.cobertura.xml>", file=sys.stderr)
        return 2

    cobertura = Path(sys.argv[1])
    if not cobertura.is_file():
        print(f"[coverage-ratchet] FAIL: cobertura ファイルがありません: {cobertura}", file=sys.stderr)
        return 1  # fail-closed: カバレッジが計測できなかったものを緑にしない

    root = ET.parse(cobertura).getroot()
    current = {m: round(float(root.attrib[f"{m}-rate"]) * 100, 2) for m in METRICS}
    baseline = json.loads(baseline_path.read_text(encoding="utf-8"))

    failed = False
    improved = False
    for metric in METRICS:
        cur, floor = current[metric], baseline[metric]
        delta = f"{cur - floor:+.2f}pt"
        if cur < floor - EPSILON:
            print(f"[coverage-ratchet] FAIL {metric}: {cur}% < baseline {floor}% ({delta})", file=sys.stderr)
            failed = True
        else:
            print(f"[coverage-ratchet] OK   {metric}: {cur}% (baseline {floor}%, {delta})")
            if cur > floor + EPSILON:
                improved = True

    if failed:
        print(
            "[coverage-ratchet] カバレッジが baseline から退行しています。"
            "テストを追加するか、意図的な低下なら coverage-baseline.json を根拠付きで更新してください。",
            file=sys.stderr,
        )
        return 1

    if improved:
        if os.environ.get("RATCHET_UPDATE") == "1":
            baseline_path.write_text(json.dumps(current, indent=2) + "\n", encoding="utf-8")
            print("[coverage-ratchet] baseline を現在値へ引き上げました。coverage-baseline.json をコミットしてください。")
        else:
            print("[coverage-ratchet] baseline より改善しています。RATCHET_UPDATE=1 で引き上げられます。")

    return 0


if __name__ == "__main__":
    sys.exit(main())
