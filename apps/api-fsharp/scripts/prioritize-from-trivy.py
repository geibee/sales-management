#!/usr/bin/env python3
"""Trivy SARIF から脆弱な NuGet パッケージを抽出し、renovate.json に優先化ルールを追記する。

使い方:
    python3 scripts/prioritize-from-trivy.py <trivy.sarif> <renovate.json>

Trivy SARIF の `runs[].results[].locations[0].logicalLocations[0].fullyQualifiedName`
や `message.text` には `<purl>` 形式の package 名が含まれるが、安定取得のため
`runs[].tool.driver.rules[].properties.tags` から `package_name=<name>` を拾う。

renovate.json には以下のルールを追記する (重複しない場合のみ):

    {
      "description": "security-critical: trivy が <date> に検出",
      "matchPackageNames": [<vulnerable packages>],
      "schedule": ["at any time"],
      "automerge": true,
      "labels": ["security-critical"]
    }
"""
import json
import pathlib
import re
import sys
from datetime import date


def extract_package_names(sarif: dict) -> list[str]:
    pkgs: set[str] = set()
    for run in sarif.get("runs", []) or []:
        for r in run.get("results", []) or []:
            text = ((r.get("message") or {}).get("text") or "")
            # Trivy は "Package: <name>" や purl 形式 (pkg:nuget/Foo@1.0.0) を含むことが多い
            for m in re.finditer(r"pkg:nuget/([^@\s]+)", text):
                pkgs.add(m.group(1))
            for m in re.finditer(r"Package:\s*([^\s,]+)", text):
                pkgs.add(m.group(1))
    return sorted(pkgs)


def main() -> int:
    if len(sys.argv) != 3:
        print(f"usage: {sys.argv[0]} <trivy.sarif> <renovate.json>", file=sys.stderr)
        return 2

    sarif_path = pathlib.Path(sys.argv[1])
    renovate_path = pathlib.Path(sys.argv[2])

    if not sarif_path.exists():
        print(f"[prioritize-from-trivy] sarif not found: {sarif_path}", file=sys.stderr)
        return 0
    if not renovate_path.exists():
        print(f"[prioritize-from-trivy] renovate.json not found: {renovate_path}", file=sys.stderr)
        return 0

    sarif = json.loads(sarif_path.read_text())
    pkgs = extract_package_names(sarif)
    if not pkgs:
        print("[prioritize-from-trivy] no vulnerable NuGet packages detected; renovate.json unchanged")
        return 0

    renovate = json.loads(renovate_path.read_text())
    rules = renovate.setdefault("packageRules", [])

    description = f"security-critical: trivy が {date.today().isoformat()} に検出"
    if any(r.get("description") == description and sorted(r.get("matchPackageNames") or []) == pkgs for r in rules):
        print(f"[prioritize-from-trivy] rule already present for {pkgs}; renovate.json unchanged")
        return 0

    rules.append({
        "description": description,
        "matchPackageNames": pkgs,
        "schedule": ["at any time"],
        "automerge": True,
        "labels": ["security-critical"],
    })

    renovate_path.write_text(json.dumps(renovate, indent=2, ensure_ascii=False) + "\n")
    print(f"[prioritize-from-trivy] appended rule for {len(pkgs)} package(s): {pkgs}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
