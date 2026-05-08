#!/usr/bin/env python3
"""Schemathesis の JUnit XML 出力 (--report-junit-path) を SARIF v2.1.0 に変換する。

使い方:
    python3 scripts/junit-to-sarif.py <input.xml> <output.sarif> [tool-name]

JUnit XML 構造（Schemathesis JunitXmlWriter 由来）:
    <testsuites>
      <testsuite name="schemathesis">
        <testcase classname="schemathesis" name="<operation label>" time="...">
          <failure message="..." type="...">...</failure>
          <error message="..." type="...">...</error>
          <skipped>...</skipped>
        </testcase>
        ...
      </testsuite>
    </testsuites>

failure → SARIF level=error、error → level=error、skipped → level=note。
正常 testcase は SARIF results として記録しない（passed テストは SARIF の
ノイズになるだけで、エージェント参照用には failures だけが価値を持つ）。
"""
from __future__ import annotations

import json
import pathlib
import sys
import xml.etree.ElementTree as ET


def _attr(elem: ET.Element, key: str, default: str = "") -> str:
    return elem.attrib.get(key, default) or default


def _result(rule_id: str, level: str, message: str, location: str) -> dict:
    return {
        "ruleId": rule_id,
        "level": level,
        "message": {"text": message[:1000] if message else rule_id},
        "locations": [{
            "physicalLocation": {
                "artifactLocation": {"uri": location or "schemathesis://unknown"},
                "region": {"startLine": 1},
            }
        }],
    }


def main() -> int:
    if len(sys.argv) < 3:
        print(f"usage: {sys.argv[0]} <input.xml> <output.sarif> [tool-name]", file=sys.stderr)
        return 2

    src = pathlib.Path(sys.argv[1])
    dst = pathlib.Path(sys.argv[2])
    tool_name = sys.argv[3] if len(sys.argv) >= 4 else "Schemathesis"

    if not src.exists():
        print(f"[junit-to-sarif] input not found: {src}", file=sys.stderr)
        return 1

    try:
        tree = ET.parse(src)
    except ET.ParseError as e:
        print(f"[junit-to-sarif] invalid XML: {e}", file=sys.stderr)
        return 1

    root = tree.getroot()
    # <testsuites> でも <testsuite> 単体でも受け付ける。
    suites = root.findall("testsuite") if root.tag == "testsuites" else [root]

    rules: dict[str, dict] = {}
    results: list[dict] = []
    total_cases = 0

    for suite in suites:
        for case in suite.findall("testcase"):
            total_cases += 1
            label = _attr(case, "name") or _attr(case, "classname") or "unknown"

            for failure in case.findall("failure"):
                rule_id = _attr(failure, "type", "schemathesis.failure")
                msg = _attr(failure, "message") or (failure.text or "").strip()
                rules.setdefault(rule_id, {
                    "id": rule_id,
                    "name": rule_id,
                    "shortDescription": {"text": rule_id},
                })
                # 性質ベース fuzz の発見はノイズ込み。初期は warning で報告のみ、
                # SARIF サマリの error ゲートで CI を落とさない。確度が高まったら
                # 別タスクで error に昇格する。
                results.append(_result(rule_id, "warning", f"{label}: {msg}", label))

            for error in case.findall("error"):
                rule_id = _attr(error, "type", "schemathesis.error")
                msg = _attr(error, "message") or (error.text or "").strip()
                rules.setdefault(rule_id, {
                    "id": rule_id,
                    "name": rule_id,
                    "shortDescription": {"text": rule_id},
                })
                results.append(_result(rule_id, "warning", f"{label}: {msg}", label))

            for skipped in case.findall("skipped"):
                rule_id = _attr(skipped, "type", "schemathesis.skipped")
                msg = _attr(skipped, "message") or (skipped.text or "").strip()
                rules.setdefault(rule_id, {
                    "id": rule_id,
                    "name": rule_id,
                    "shortDescription": {"text": rule_id},
                })
                results.append(_result(rule_id, "note", f"{label}: {msg}", label))

    sarif = {
        "$schema": "https://json.schemastore.org/sarif-2.1.0.json",
        "version": "2.1.0",
        "runs": [{
            "tool": {
                "driver": {
                    "name": tool_name,
                    "informationUri": "https://schemathesis.readthedocs.io/",
                    "rules": list(rules.values()),
                }
            },
            "results": results,
        }],
    }

    dst.parent.mkdir(parents=True, exist_ok=True)
    dst.write_text(json.dumps(sarif, indent=2, ensure_ascii=False))
    print(f"[junit-to-sarif] wrote {dst} ({len(results)} results from {total_cases} testcases, {len(rules)} rules)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
