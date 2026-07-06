#!/usr/bin/env python3
"""品質ラチェット — 「前回値から悪化したら失敗」方式のしきい値ゲート。

フロントエンドの coverage-ratchet.mjs と同じ思想のバックエンド版。
固定しきい値は「最初から高い目標を置けず、置いた後は形骸化する」ため、
quality-baseline.json に記録した実測値を基準として悪化のみを検出する。

  usage: quality-ratchet.py <metric> <current-value>

  metric:
    coverage_line_rate       行カバレッジ (0.0-1.0、高いほど良い)
    fsharplint_warnings      FSharpLint 警告数 (低いほど良い)
    scc_max_file_complexity  scc の最大ファイル複雑度 (低いほど良い)

動作:
  - baseline が null → bootstrap モード: 現在値を表示して合格 (初回計測用)。
    値を quality-baseline.json にコミットした時点からゲートが有効になる
  - 悪化 (許容誤差超) → exit 1
  - 改善 → 合格。RATCHET_UPDATE=1 なら baseline を現在値へ更新する
    (更新後の quality-baseline.json はコミットすること)
"""

import json
import os
import pathlib
import sys

BASELINE_PATH = pathlib.Path(__file__).resolve().parent.parent / "quality-baseline.json"

# metric 名 → (方向, 許容誤差)。誤差は計測揺れの吸収用
METRICS = {
    "coverage_line_rate": ("higher", 0.005),
    "fsharplint_warnings": ("lower", 0),
    "scc_max_file_complexity": ("lower", 0),
}


def main() -> int:
    if len(sys.argv) != 3 or sys.argv[1] not in METRICS:
        print(__doc__, file=sys.stderr)
        return 2
    metric, raw = sys.argv[1], sys.argv[2]
    direction, epsilon = METRICS[metric]
    current = float(raw)

    baseline_doc = json.loads(BASELINE_PATH.read_text())
    floor = baseline_doc.get(metric)

    if floor is None:
        print(f"[quality-ratchet] BOOTSTRAP {metric}: current={current:g} "
              f"(baseline 未設定。この値を quality-baseline.json に記録するとゲートが有効になる)")
        return 0

    floor = float(floor)
    if direction == "higher":
        regressed = current < floor - epsilon
        improved = current > floor + epsilon
    else:
        regressed = current > floor + epsilon
        improved = current < floor - epsilon

    delta = current - floor
    if regressed:
        print(f"[quality-ratchet] FAIL {metric}: {current:g} が baseline {floor:g} から悪化 "
              f"(delta={delta:+g})。改善するか、意図的なら quality-baseline.json を根拠付きで更新すること",
              file=sys.stderr)
        return 1

    print(f"[quality-ratchet] OK   {metric}: {current:g} (baseline {floor:g}, delta={delta:+g})")
    if improved:
        if os.environ.get("RATCHET_UPDATE") == "1":
            baseline_doc[metric] = int(current) if current.is_integer() else current
            BASELINE_PATH.write_text(json.dumps(baseline_doc, indent=2, ensure_ascii=False) + "\n")
            print(f"[quality-ratchet] baseline を {current:g} へ更新。quality-baseline.json をコミットすること")
        else:
            print(f"[quality-ratchet] baseline より改善。RATCHET_UPDATE=1 で引き上げ可能")
    return 0


if __name__ == "__main__":
    sys.exit(main())
