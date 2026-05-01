#!/usr/bin/env python3
"""複数の SARIF v2.1.0 ファイルを 1 ファイルに連結する。

使い方:
    python3 scripts/sarif-merge.py <output.sarif> <input1.sarif> [input2.sarif ...]

Sarif.Multitool の merge は空 run 削除や results 重複排除を行うため、
PoC のエージェント参照用には素朴な runs[] 連結が望ましい。本スクリプトは
入力ファイルが存在しない場合はスキップし、各ファイルの runs[] をそのまま
順番に結合した SARIF を出力する。
"""
import json
import pathlib
import sys


def main() -> int:
    if len(sys.argv) < 3:
        print(f"usage: {sys.argv[0]} <output.sarif> <input1.sarif> [input2.sarif ...]", file=sys.stderr)
        return 2

    out_path = pathlib.Path(sys.argv[1])
    inputs = [pathlib.Path(p) for p in sys.argv[2:]]

    runs: list[dict] = []
    for src in inputs:
        if not src.exists():
            print(f"[sarif-merge] skip missing: {src}", file=sys.stderr)
            continue
        try:
            data = json.loads(src.read_text())
        except json.JSONDecodeError as e:
            print(f"[sarif-merge] skip invalid: {src} ({e})", file=sys.stderr)
            continue
        runs.extend(data.get("runs", []) or [])

    merged = {
        "$schema": "https://json.schemastore.org/sarif-2.1.0.json",
        "version": "2.1.0",
        "runs": runs,
    }

    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(json.dumps(merged, indent=2, ensure_ascii=False))
    total_results = sum(len(r.get("results", []) or []) for r in runs)
    print(f"[sarif-merge] wrote {out_path} ({len(runs)} runs, {total_results} results)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
