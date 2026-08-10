#!/usr/bin/env python3
"""Nightly の XML/JSON 成果物を fail-closed な SARIF へ変換する。"""

from __future__ import annotations

import argparse
import glob
import json
import pathlib
import xml.etree.ElementTree as ET


def result(rule_id: str, level: str, message: str, uri: str) -> dict:
    return {
        "ruleId": rule_id,
        "level": level,
        "message": {"text": message[:4000]},
        "locations": [
            {
                "physicalLocation": {
                    "artifactLocation": {"uri": uri},
                    "region": {"startLine": 1},
                }
            }
        ],
    }


def require_files(patterns: list[str]) -> list[pathlib.Path]:
    paths = sorted({pathlib.Path(item) for pattern in patterns for item in glob.glob(pattern)})
    if not paths:
        raise SystemExit(f"必須レポートがありません: {patterns}")
    return paths


def junit(paths: list[pathlib.Path]) -> list[dict]:
    findings: list[dict] = []
    cases = 0
    for path in paths:
        root = ET.parse(path).getroot()
        suites = root.findall("testsuite") if root.tag == "testsuites" else [root]
        for suite in suites:
            for case in suite.findall("testcase"):
                cases += 1
                name = case.attrib.get("name", "unknown")
                for tag in ("failure", "error", "skipped"):
                    for node in case.findall(tag):
                        rule_id = node.attrib.get("type", f"junit.{tag}")
                        message = node.attrib.get("message") or (node.text or tag)
                        findings.append(result(rule_id, "error", f"{name}: {message}", str(path)))
    if cases == 0:
        raise SystemExit("JUnit レポートに testcase がありません")
    return findings


def spotbugs(paths: list[pathlib.Path]) -> list[dict]:
    findings: list[dict] = []
    for path in paths:
        root = ET.parse(path).getroot()
        for bug in root.findall(".//BugInstance"):
            rule_id = bug.attrib.get("type", "spotbugs.unknown")
            source = bug.find("SourceLine")
            uri = source.attrib.get("sourcepath", str(path)) if source is not None else str(path)
            findings.append(result(rule_id, "error", bug.attrib.get("message", rule_id), uri))
    return findings


def pit(paths: list[pathlib.Path]) -> list[dict]:
    findings: list[dict] = []
    mutations = 0
    for path in paths:
        root = ET.parse(path).getroot()
        for mutation in root.findall(".//mutation"):
            mutations += 1
            status = mutation.attrib.get("status", "UNKNOWN")
            if status == "KILLED":
                continue
            source = mutation.findtext("sourceFile", default=str(path))
            description = mutation.findtext("description", default=status)
            level = "error" if status in {"TIMED_OUT", "RUN_ERROR", "MEMORY_ERROR"} else "warning"
            findings.append(result(f"pit.{status}", level, description, source))
    if mutations == 0:
        raise SystemExit("PIT レポートに mutation がありません")
    return findings


def artifact(paths: list[pathlib.Path]) -> list[dict]:
    for path in paths:
        if path.stat().st_size == 0:
            raise SystemExit(f"成果物が空です: {path}")
        if path.suffix == ".json":
            data = json.loads(path.read_text(encoding="utf-8"))
            if "bomFormat" in data and not data.get("components"):
                raise SystemExit(f"SBOM に component がありません: {path}")
    return []


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--tool", required=True)
    parser.add_argument("--kind", choices=("junit", "spotbugs", "pit", "artifact"), required=True)
    parser.add_argument("--input", action="append", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()

    paths = require_files(args.input)
    findings = {
        "junit": junit,
        "spotbugs": spotbugs,
        "pit": pit,
        "artifact": artifact,
    }[args.kind](paths)
    document = {
        "$schema": "https://json.schemastore.org/sarif-2.1.0.json",
        "version": "2.1.0",
        "runs": [
            {
                "tool": {"driver": {"name": args.tool}},
                "invocations": [{"executionSuccessful": True}],
                "results": findings,
            }
        ],
    }
    output = pathlib.Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(document, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"{args.tool}: {len(findings)} findings -> {output}")


if __name__ == "__main__":
    main()
