#!/usr/bin/env python3
"""今回のネイティブレポートを保存し、共通保証の監査を実行する。"""

import argparse
import json
from pathlib import Path
import runpy
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--target", choices=("fsharp", "spring"), required=True)
    parser.add_argument("--directory", type=Path, required=True)
    parser.add_argument("--profile", choices=("light", "full"), required=True)
    parser.add_argument("--collect-only", action="store_true")
    args = parser.parse_args()
    directory = args.directory.resolve()
    context = json.loads((directory / "run.json").read_text())
    checker = runpy.run_path(str(ROOT / "scripts/assurance-audit.py"))["check_freshness"]
    catalog_path = ROOT / "quality/guarantees.json"
    catalog = json.loads(catalog_path.read_text())
    native = ROOT / "apps" / ("api-fsharp" if args.target == "fsharp" else "api-spring")
    reports = list(native.glob("coverage/*.trx" if args.target == "fsharp" else "*/target/surefire-reports/TEST-*.xml"))
    saved = []
    errors = []
    evidence_dir = directory / "evidence"
    evidence_dir.mkdir(exist_ok=True)
    snapshot = directory / "snapshot.json"
    if snapshot.is_file() and not args.collect_only:
        state = json.loads(snapshot.read_text())
        saved = [Path(filename) for filename in state["reports"]]
        errors = state["errors"]
    else:
        for index, report in enumerate(sorted(reports)):
            try:
                checker(report, context)
                destination = evidence_dir / f"{index}-{report.name}"
                shutil.copy2(report, destination)
                saved.append(destination)
            except (OSError, ValueError) as exception:
                errors.append(str(exception))
        metrics = {}
        try:
            if args.target == "fsharp":
                coverage = list(native.glob("coverage/**/coverage.cobertura.xml"))
                if len(coverage) != 1:
                    raise ValueError(f"Cobertura は1件必須です: {len(coverage)}")
                checker(coverage[0], context)
                counter = ET.parse(coverage[0]).getroot().attrib
                metrics = {"lineCoverage": float(counter["line-rate"]) * 100,
                           "branchCoverage": float(counter["branch-rate"]) * 100}
                operation_path = native / "coverage/operation-coverage.json"
                shutil.copy2(coverage[0], evidence_dir / "coverage.cobertura.xml")
            else:
                # 生成 contracts / 空の test-support は手書き実装の計測母数に含めない。
                for module in ("domain", "application", "infrastructure", "api", "batch", "tools"):
                    coverage = native / module / "target/site/jacoco/jacoco.xml"
                    checker(coverage, context)
                    shutil.copy2(coverage, evidence_dir / f"jacoco-{module}.xml")
                ratchet = runpy.run_path(str(native / "scripts/verify-quality-ratchets.py"))
                metrics = {"lineCoverage": ratchet["coverage_percent"]("LINE"),
                           "branchCoverage": ratchet["coverage_percent"]("BRANCH")}
                operation_path = native / "api/target/operation-coverage.json"
            checker(operation_path, context)
            operations = json.loads(operation_path.read_text())
            metrics["operationCount"] = len(set(operations))
            shutil.copy2(operation_path, evidence_dir / "operation-coverage.json")
        except (OSError, ValueError, KeyError, ET.ParseError, SystemExit) as exception:
            errors.append(str(exception))
        (directory / "metrics.json").write_text(json.dumps(metrics, indent=2) + "\n")
        snapshot.write_text(json.dumps({"reports": [str(path) for path in saved], "errors": errors}) + "\n")
    if args.collect_only:
        for error in errors:
            print(error, file=sys.stderr)
        return 1 if errors else 0
    tools = catalog["tools"][args.target]
    required_ids = {item["toolId"] for guarantee in catalog["guarantees"]
                    if args.profile == "full" or guarantee.get("tier", "light") == "light"
                    for item in guarantee["evidence"][args.target] if item["type"] == "sarif"}
    # 言語固有の追加検査も全量の必須条件として保持する。
    required_ids.update(catalog.get("additionalTools", {}).get(args.target, []) if args.profile == "full" else [])
    tool_spec = {"tools": [{"id": key, "tool": tools[key], "file": f"{key}.sarif"} for key in sorted(required_ids)]}
    (directory / "tools.json").write_text(json.dumps(tool_spec, indent=2) + "\n")
    sarif_status = subprocess.run([sys.executable, str(ROOT / "scripts/sarif-audit.py"),
        "--spec", str(directory / "tools.json"), "--sarif-dir", str(directory / "sarif"),
        "--run-context", str(directory / "run.json"), "--manifest", str(directory / "sarif-manifest.json"),
        "--merged", str(directory / "merged.sarif")], check=False).returncode
    command = [sys.executable, str(ROOT / "scripts/assurance-audit.py"), "--catalog", str(catalog_path),
        "--target", args.target, "--profile", args.profile, "--run-context", str(directory / "run.json"),
        "--metrics", str(directory / "metrics.json"), "--sarif-manifest", str(directory / "sarif-manifest.json"),
        "--manifest", str(directory / "assurance-manifest.json"), "--sarif-output", str(directory / "sarif/assurance.sarif")]
    for path in saved:
        command.extend(["--test-report", str(path)])
    audit_status = subprocess.run(command, check=False).returncode
    merged_path = directory / "merged.sarif"
    merged = json.loads(merged_path.read_text())
    merged["runs"].extend(json.loads((directory / "sarif/assurance.sarif").read_text())["runs"])
    if errors:
        merged["runs"].append({"tool": {"driver": {"name": "Evidence collection"}}, "results": [
            {"ruleId": "evidence.invalid", "level": "error", "message": {"text": message}} for message in errors]})
    merged_path.write_text(json.dumps(merged, ensure_ascii=False, indent=2) + "\n")
    # 既存の成果物・LESSONS 消費者向け互換出力。
    compatibility = native / "ci-results"
    compatibility.mkdir(exist_ok=True)
    shutil.copy2(merged_path, compatibility / "merged.sarif")
    shutil.copy2(merged_path, ROOT / "ci-results/merged.sarif")
    print(f"品質保証 {args.target}/{args.profile}: {directory}")
    for error in errors:
        print(error, file=sys.stderr)
    return 1 if errors or audit_status or sarif_status else 0


if __name__ == "__main__":
    sys.exit(main())
