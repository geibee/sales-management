#!/usr/bin/env python3
"""隔離コピーに共通故障を注入し、対象テストの assertion による検出を必須にする。"""

import argparse
import json
import os
from pathlib import Path
import runpy
import shutil
import subprocess
import tempfile


ROOT = Path(__file__).resolve().parents[1]


def inject(root: Path, fault: dict) -> str:
    path = root / fault["file"]
    original = path.read_text()
    if original.count(fault["before"]) != 1:
        raise ValueError(f"故障の適用箇所は1件必須です: {fault['file']}")
    path.write_text(original.replace(fault["before"], fault["after"], 1))
    return original


def detected(cases: list[tuple[str, bool]], log: str) -> bool:
    # ツール・コンパイル・環境障害を変異検出に数えない。
    return bool(cases) and any(not passed for _, passed in cases) and any(
        marker in log for marker in ("Xunit.Sdk.", "Assert.Equal() Failure", "Assert.True() Failure",
                                    "Falsifiable", "AssertionFailedError", "AssertionError"))


def execute(root: Path, target: str, fault: dict, log_path: Path) -> tuple[int, list, str]:
    application = root / "apps" / ("api-fsharp" if target == "fsharp" else "api-spring")
    if target == "fsharp":
        report_dir = application / "fault-results"
        shutil.rmtree(report_dir, ignore_errors=True)
        command = ["dotnet", "test", "tests/SalesManagement.Tests", "--filter", fault["filter"],
                   "--logger:trx;LogFileName=fault.trx", "--results-directory", str(report_dir)]
    else:
        for directory in application.glob("*/target/surefire-reports"):
            shutil.rmtree(directory)
        command = ["./mvnw", "-B", "-pl", fault["module"], "-am", "-Dtest=" + fault["filter"],
                   "-Dsurefire.failIfNoSpecifiedTests=false", "test"]
    environment = {**os.environ, "PACT_BROKER_URL": "", "pact_do_not_track": "true"}
    with log_path.open("w") as output:
        try:
            status = subprocess.run(command, cwd=application, env=environment, stdout=output,
                                    stderr=subprocess.STDOUT, timeout=300, check=False).returncode
        except subprocess.TimeoutExpired:
            return 124, [], "timeout"
    read_tests = runpy.run_path(str(ROOT / "scripts/assurance-audit.py"))["read_tests"]
    reports = application.glob("fault-results/*.trx" if target == "fsharp" else "*/target/surefire-reports/TEST-*.xml")
    cases = [case for report in reports for case in read_tests(report)]
    return status, cases, log_path.read_text(errors="replace")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--target", choices=("fsharp", "spring"), required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    faults = json.loads((ROOT / "quality/faults.json").read_text())
    args.output.parent.mkdir(parents=True, exist_ok=True)
    results = []
    # 作業ツリーの未コミット変更も含め、ignore 対象の成果物・キャッシュはコピーしない。
    files = subprocess.check_output(["git", "ls-files", "-c", "-o", "--exclude-standard", "-z"], cwd=ROOT).decode().split("\0")
    with tempfile.TemporaryDirectory(prefix="sales-quality-faults-") as temporary:
        snapshot = Path(temporary)
        for filename in set(files) - {""}:
            source = ROOT / filename
            if source.is_file():
                destination = snapshot / filename
                destination.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(source, destination)
        for entry in faults:
            fault = entry[args.target]
            baseline_log = args.output.parent / f"fault-{entry['id']}-baseline.log"
            status, cases, _ = execute(snapshot, args.target, fault, baseline_log)
            if status or not cases or not all(passed for _, passed in cases):
                results.append({"id": entry["id"], "detected": False, "reason": "故障なしの対照テストが失敗"})
                break
            original = inject(snapshot, fault)
            try:
                log_path = args.output.parent / f"fault-{entry['id']}.log"
                status, cases, log = execute(snapshot, args.target, fault, log_path)
                killed = status != 0 and detected(cases, log)
                results.append({"id": entry["id"], "detected": killed, "tests": len(cases), "exitCode": status})
                print(f"fault {args.target}/{entry['id']}: {'detected' if killed else 'FAILED'}", flush=True)
            finally:
                (snapshot / fault["file"]).write_text(original)
    args.output.write_text(json.dumps({"target": args.target, "faults": results}, ensure_ascii=False, indent=2) + "\n")
    if len(results) != len(faults) or not all(item["detected"] for item in results):
        raise SystemExit("共通故障の検出が不足しています")


if __name__ == "__main__":
    main()
