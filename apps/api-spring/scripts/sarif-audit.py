#!/usr/bin/env python3
"""必須 SARIF の存在・tool 名・hash・件数・skip・error を監査して統合する。"""

from __future__ import annotations

import argparse
import hashlib
import json
import pathlib


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--spec", required=True)
    parser.add_argument("--sarif-dir", required=True)
    parser.add_argument("--manifest", required=True)
    parser.add_argument("--merged", required=True)
    args = parser.parse_args()

    expected = json.loads(pathlib.Path(args.spec).read_text(encoding="utf-8"))["tools"]
    sarif_dir = pathlib.Path(args.sarif_dir)
    entries: list[dict] = []
    merged_runs: list[dict] = []
    errors: list[str] = []

    for item in expected:
        path = sarif_dir / item["file"]
        if not path.is_file():
            errors.append(f"必須 SARIF がありません: {path}")
            continue
        raw = path.read_bytes()
        try:
            document = json.loads(raw)
        except json.JSONDecodeError as exception:
            errors.append(f"SARIF JSON が不正です: {path}: {exception}")
            continue
        if document.get("version") != "2.1.0" or not document.get("runs"):
            errors.append(f"SARIF v2.1.0 run がありません: {path}")
            continue
        tool_names = {
            run.get("tool", {}).get("driver", {}).get("name") for run in document["runs"]
        }
        if tool_names != {item["tool"]}:
            errors.append(f"tool 名不一致: {path}: {sorted(str(name) for name in tool_names)}")
        results = [result for run in document["runs"] for result in run.get("results", [])]
        for run in document["runs"]:
            if run.get("properties", {}).get("skipped") is True:
                errors.append(f"skip を検出しました: {path}")
            for invocation in run.get("invocations", []):
                if invocation.get("executionSuccessful") is False:
                    errors.append(f"実行失敗 invocation を検出しました: {path}")
        error_count = sum(result.get("level") == "error" for result in results)
        if error_count:
            errors.append(f"error finding が {error_count} 件あります: {path}")
        entries.append(
            {
                "id": item["id"],
                "tool": item["tool"],
                "path": str(path),
                "sha256": hashlib.sha256(raw).hexdigest(),
                "results": len(results),
                "errors": error_count,
            }
        )
        merged_runs.extend(document["runs"])

    manifest = {"version": 1, "tools": entries}
    pathlib.Path(args.manifest).write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    pathlib.Path(args.merged).write_text(
        json.dumps(
            {
                "$schema": "https://json.schemastore.org/sarif-2.1.0.json",
                "version": "2.1.0",
                "runs": merged_runs,
            },
            ensure_ascii=False,
            indent=2,
        ),
        encoding="utf-8",
    )
    if len(entries) != len(expected):
        errors.append(f"必須 tool 数が不足: {len(entries)}/{len(expected)}")
    if errors:
        raise SystemExit("\n".join(errors))
    print(f"SARIF audit: {len(entries)} tools, errors=0")


if __name__ == "__main__":
    main()
