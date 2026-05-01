#!/usr/bin/env python3
"""FSharpLint のテキスト出力を SARIF v2.1.0 に変換する。

使い方:
    python3 scripts/lint-to-sarif.py <input.txt> <output.sarif>

FSharpLint は SARIF をネイティブで出さないため、`dotnet dotnet-fsharplint lint <project>` の
標準出力をテキストファイルに保存してから本スクリプトで変換する。
出力例:
    src/SalesManagement/Domain/Types.fs(42,5): warning RuleName: <message>
    ========== Finished: 0 warnings ==========
"""
import json
import pathlib
import re
import sys

WARNING_RE = re.compile(
    r"^(?P<file>[^()\n]+)\((?P<line>\d+),(?P<col>\d+)\):\s*"
    r"(?P<sev>warning|error|info)\s+(?P<rule>[^:]+?):\s*(?P<msg>.+)$"
)


def main() -> int:
    if len(sys.argv) != 3:
        print(f"usage: {sys.argv[0]} <input.txt> <output.sarif>", file=sys.stderr)
        return 2

    src = pathlib.Path(sys.argv[1])
    dst = pathlib.Path(sys.argv[2])
    if not src.exists():
        print(f"[lint-to-sarif] input not found: {src}", file=sys.stderr)
        return 1

    rules: dict[str, dict] = {}
    results: list[dict] = []

    for line in src.read_text(errors="replace").splitlines():
        m = WARNING_RE.match(line.strip())
        if not m:
            continue
        rule = m.group("rule").strip()
        sev = m.group("sev").lower()
        level = "error" if sev == "error" else ("note" if sev == "info" else "warning")
        if rule not in rules:
            rules[rule] = {"id": rule, "name": rule, "shortDescription": {"text": rule}}
        results.append({
            "ruleId": rule,
            "level": level,
            "message": {"text": m.group("msg").strip()[:500]},
            "locations": [{
                "physicalLocation": {
                    "artifactLocation": {"uri": m.group("file").strip()},
                    "region": {
                        "startLine": int(m.group("line")),
                        "startColumn": int(m.group("col")),
                    },
                }
            }],
        })

    sarif = {
        "$schema": "https://json.schemastore.org/sarif-2.1.0.json",
        "version": "2.1.0",
        "runs": [{
            "tool": {
                "driver": {
                    "name": "FSharpLint",
                    "informationUri": "https://fsprojects.github.io/FSharpLint/",
                    "rules": list(rules.values()),
                }
            },
            "results": results,
        }],
    }

    dst.parent.mkdir(parents=True, exist_ok=True)
    dst.write_text(json.dumps(sarif, indent=2, ensure_ascii=False))
    print(f"[lint-to-sarif] wrote {dst} ({len(results)} results, {len(rules)} rules)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
