#!/usr/bin/env python3
"""共通保証を今回実行したテスト・SARIF・計測値に照合する。"""

from __future__ import annotations

import argparse
import fnmatch
import hashlib
import json
import math
from pathlib import Path
import xml.etree.ElementTree as ET


def check_freshness(path: Path, context: dict) -> None:
    if context and path.stat().st_mtime_ns < context["startedNs"]:
        raise ValueError(f"実行開始より古い成果物です: {path}")


def read_tests(path: Path) -> list[tuple[str, bool]]:
    root = ET.parse(path).getroot()
    # TRX の名前空間はバージョンに依存しない形で扱う。
    for element in root.iter():
        element.tag = element.tag.rsplit("}", 1)[-1]
    if root.tag == "TestRun":
        identities = {}
        for test in root.findall(".//TestDefinitions/UnitTest"):
            method = test.find("TestMethod")
            if method is not None:
                identities[test.attrib["id"]] = f"{method.attrib['className']}#{method.attrib['name']}"
        return [
            (identities[result.attrib["testId"]], result.attrib.get("outcome") == "Passed")
            for result in root.findall(".//Results/UnitTestResult")
        ]
    if root.tag not in {"testsuite", "testsuites"}:
        raise ValueError(f"未対応のテストレポートです: {path}")
    return [
        (f"{case.attrib['classname']}#{case.attrib['name']}",
         not any(case.find(tag) is not None for tag in ("failure", "error", "skipped")))
        for case in root.iter("testcase")
    ]


def validate_catalog(catalog: dict) -> None:
    if catalog.get("version") != 1 or set(catalog.get("targets", [])) != {"fsharp", "spring"}:
        raise ValueError("catalog は version=1 と fsharp/spring の両 target が必須です")
    guarantees = catalog.get("guarantees")
    if not isinstance(guarantees, list) or not guarantees:
        raise ValueError("保証一覧が空です")
    seen = set()
    for guarantee in guarantees:
        if guarantee["id"] in seen:
            raise ValueError(f"保証 ID が重複しています: {guarantee['id']}")
        seen.add(guarantee["id"])
        if set(guarantee["requiredTargets"]) != {"fsharp", "spring"}:
            raise ValueError(f"共通保証は両 target が必須です: {guarantee['id']}")
        if guarantee.get("tier", "light") not in {"light", "heavy"}:
            raise ValueError("未知の tier です")
        for target in guarantee["requiredTargets"]:
            evidence = guarantee["evidence"].get(target)
            if not isinstance(evidence, list) or not evidence:
                raise ValueError(f"証拠定義がありません: {guarantee['id']}/{target}")
            for item in evidence:
                if item.get("type") == "test":
                    if not item.get("selector") or item.get("minimumCases", 1) < 1:
                        raise ValueError("テストの selector と正の minimumCases が必要です")
                elif item.get("type") == "sarif":
                    if not item.get("toolId"):
                        raise ValueError("SARIF の toolId が必要です")
                else:
                    raise ValueError("証拠は実行テストまたは SARIF に限定します")


def audit_evidence(item: dict, tests: list[tuple[str, bool]], tools: dict) -> str | None:
    if item["type"] == "test":
        matched = [passed for name, passed in tests if fnmatch.fnmatchcase(name, item["selector"])]
        if len(matched) < item.get("minimumCases", 1) or not all(matched):
            return f"必須テストが不足・失敗・skip: {item['selector']} (実行 {len(matched)})"
    else:
        report = tools.get(item["toolId"])
        if (report is None or report.get("errors") != 0 or report.get("skipped", False)
                or report.get("executionSuccessful", True) is not True):
            return f"必須 tool が不足または失敗: {item['toolId']}"
    return None


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--catalog", required=True)
    parser.add_argument("--target", choices=("fsharp", "spring"), required=True)
    parser.add_argument("--profile", choices=("light", "full"), default="full")
    parser.add_argument("--test-report", action="append", default=[])
    parser.add_argument("--metrics")
    parser.add_argument("--sarif-manifest")
    parser.add_argument("--run-context")
    parser.add_argument("--manifest", required=True)
    parser.add_argument("--sarif-output", required=True)
    args = parser.parse_args()
    errors = []
    guarantees = []
    passed = 0
    inputs = []
    context = {}
    try:
        catalog = json.loads(Path(args.catalog).read_text())
        validate_catalog(catalog)
        context = json.loads(Path(args.run_context).read_text()) if args.run_context else {}
        tests = []
        for filename in args.test_report:
            path = Path(filename)
            check_freshness(path, context)
            cases = read_tests(path)
            if not cases:
                raise ValueError(f"テストが0件です: {path}")
            tests.extend(cases)
            inputs.append({"path": str(path), "sha256": hashlib.sha256(path.read_bytes()).hexdigest()})
        metrics = {}
        tools = {}
        for filename, kind in [(args.metrics, "metrics"), (args.sarif_manifest, "sarif")]:
            if filename:
                path = Path(filename)
                check_freshness(path, context)
                data = json.loads(path.read_text())
                if kind == "metrics":
                    metrics = data
                else:
                    for entry in data["tools"]:
                        if entry["id"] in tools:
                            raise ValueError("SARIF tool ID が重複しています")
                        tools[entry["id"]] = entry
                        if context:
                            report_path = Path(entry["path"])
                            check_freshness(report_path, context)
                            if hashlib.sha256(report_path.read_bytes()).hexdigest() != entry["sha256"]:
                                raise ValueError(f"SARIF hash が不一致です: {report_path}")
                inputs.append({"path": str(path), "sha256": hashlib.sha256(path.read_bytes()).hexdigest()})
        for guarantee in catalog["guarantees"]:
            if args.profile == "light" and guarantee.get("tier", "light") == "heavy":
                continue
            failures = [message for item in guarantee["evidence"][args.target]
                        if (message := audit_evidence(item, tests, tools))]
            guarantees.append({"id": guarantee["id"], "passed": not failures, "failures": failures})
            errors.extend(f"{guarantee['id']}: {message}" for message in failures)
            passed += not failures
        for metric in catalog.get("metrics", []):
            value = metrics.get(metric["id"])
            minimum = metric["minimum"][args.target]
            valid = (type(value) in (int, float) and math.isfinite(value) and value >= 0
                     and value >= minimum)
            if valid:
                passed += 1
            else:
                errors.append(f"{metric['id']}: 計測欠落・不正・下限未満 ({value} < {minimum})")
    except (OSError, ValueError, KeyError, TypeError, ET.ParseError) as exception:
        errors.append(str(exception))
    manifest = {"version": 1, "target": args.target, "profile": args.profile, "run": context,
                "guarantees": guarantees, "inputs": inputs,
                "summary": {"passed": passed, "failed": len(errors)}}
    sarif = {"version": "2.1.0", "runs": [{
        "tool": {"driver": {"name": "Assurance audit"}},
        "invocations": [{"executionSuccessful": not errors}],
        "results": [{"ruleId": "assurance.incomplete", "level": "error", "message": {"text": message}}
                    for message in errors],
    }]}
    for filename, data in [(args.manifest, manifest), (args.sarif_output, sarif)]:
        path = Path(filename)
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n")
    if errors:
        raise SystemExit("\n".join(errors))
    print(f"assurance {args.target}/{args.profile}: {passed} passed")


if __name__ == "__main__":
    main()
