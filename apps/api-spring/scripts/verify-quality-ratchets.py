#!/usr/bin/env python3
"""Spring のテスト数・JaCoCo line・operation coverage・mutation score を後退させない。"""

from __future__ import annotations

import argparse
import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


def fail(message: str) -> None:
    print(f"[spring-ratchet] FAIL: {message}", file=sys.stderr)
    raise SystemExit(1)


def test_count() -> int:
    total = 0
    reports = list(ROOT.glob("*/target/surefire-reports/TEST-*.xml"))
    if not reports:
        fail("Surefire XML がありません")
    for report in reports:
        root = ET.parse(report).getroot()
        total += int(root.attrib.get("tests", "0")) - int(root.attrib.get("skipped", "0"))
    return total


def line_coverage() -> float:
    covered = 0
    missed = 0
    reports = list(ROOT.glob("*/target/site/jacoco/jacoco.xml"))
    if not reports:
        fail("JaCoCo XML がありません")
    for report in reports:
        root = ET.parse(report).getroot()
        counter = next((item for item in root.findall("counter") if item.attrib["type"] == "LINE"), None)
        if counter is not None:
            covered += int(counter.attrib["covered"])
            missed += int(counter.attrib["missed"])
    if covered + missed == 0:
        fail("JaCoCo の line counter が空です")
    return covered * 100.0 / (covered + missed)


def mutation_score() -> float:
    reports = list(ROOT.glob("*/target/pit-reports/mutations.xml"))
    if not reports:
        fail("PIT mutations.xml がありません")
    killed = 0
    total = 0
    for report in reports:
        root = ET.parse(report).getroot()
        for mutation in root.findall(".//mutation"):
            total += 1
            if mutation.attrib.get("status") == "KILLED":
                killed += 1
    if total == 0:
        fail("PIT mutation が0件です")
    return killed * 100.0 / total


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--require-mutation", action="store_true")
    args = parser.parse_args()
    baseline = json.loads((ROOT / "quality-baseline.json").read_text())
    tests = test_count()
    coverage = line_coverage()
    contract = json.loads((ROOT / "parity-ledger.json").read_text())
    operations = len(contract["operations"])
    if tests < baseline["testCount"]:
        fail(f"test count {tests} < {baseline['testCount']}")
    if coverage + 0.005 < baseline["coveredLinePercent"]:
        fail(f"line coverage {coverage:.2f}% < {baseline['coveredLinePercent']:.2f}%")
    if operations < baseline["operationCount"]:
        fail(f"operation coverage {operations} < {baseline['operationCount']}")
    summary = f"tests={tests}, line={coverage:.2f}%, operations={operations}"
    if args.require_mutation:
        mutation = mutation_score()
        if mutation + 0.005 < baseline["mutationScorePercent"]:
            fail(f"mutation score {mutation:.2f}% < {baseline['mutationScorePercent']:.2f}%")
        summary += f", mutation={mutation:.2f}%"
    print(f"[spring-ratchet] OK: {summary}")


if __name__ == "__main__":
    main()
