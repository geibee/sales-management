#!/usr/bin/env python3
"""実行単位の証拠を分離し、コマンド失敗時にもログと SARIF を残す。"""

import argparse
import glob
import json
import os
from pathlib import Path
import re
import runpy
import subprocess
import sys
import time
import uuid
import xml.etree.ElementTree as ET


def write_json(path: Path, value: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n")


def main() -> int:
    parser = argparse.ArgumentParser()
    sub = parser.add_subparsers(dest="action", required=True)
    init = sub.add_parser("init")
    init.add_argument("--target", choices=("fsharp", "spring"), required=True)
    init.add_argument("--profile", choices=("light", "full"), required=True)
    init.add_argument("--root", default="ci-results")
    step = sub.add_parser("step")
    step.add_argument("--directory", required=True)
    step.add_argument("--id", required=True)
    step.add_argument("--tool", required=True)
    step.add_argument("--test-report", action="append", default=[])
    step.add_argument("--minimum-test-cases", type=int, default=1)
    step.add_argument("command", nargs=argparse.REMAINDER)
    args = parser.parse_args()
    if args.action == "init":
        run_id = f"{time.time_ns()}-{uuid.uuid4().hex[:8]}"
        root = Path(args.root).resolve()
        directory = root / "runs" / args.target / run_id
        directory.mkdir(parents=True)
        (directory / "sarif").mkdir()
        commit = subprocess.check_output(["git", "rev-parse", "HEAD"], text=True).strip()
        write_json(directory / "run.json", {"version": 1, "id": run_id, "commit": commit,
                   "target": args.target, "profile": args.profile, "startedNs": time.time_ns()})
        # 最新実行へのポインタだけを固定位置に置く。監査入力には run ディレクトリを使う。
        write_json(root / f"latest-{args.target}.json", {"directory": str(directory)})
        print(directory)
        return 0
    if not re.fullmatch(r"[a-z][a-z0-9-]*", args.id):
        parser.error("id は小文字英数字とハイフンに限定します")
    command = args.command[1:] if args.command[:1] == ["--"] else args.command
    if not command:
        parser.error("実行する command が必要です")
    directory = Path(args.directory).resolve()
    log_path = directory / "logs" / f"{args.id}.log"
    log_path.parent.mkdir(parents=True, exist_ok=True)
    code = 127
    started_ns = time.time_ns()
    with log_path.open("w") as log:
        try:
            with subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                  text=True, errors="replace", env=os.environ) as process:
                for line in process.stdout:
                    log.write(line)
                    log.flush()
                    print(line, end="", flush=True)
                code = process.wait()
        except OSError as exception:
            log.write(str(exception))
            print(exception, file=sys.stderr)
        if args.test_report:
            try:
                reader = runpy.run_path(str(Path(__file__).with_name("assurance-audit.py")))["read_tests"]
                cases = []
                for pattern in args.test_report:
                    paths = glob.glob(pattern)
                    if not paths:
                        raise ValueError(f"必須テストレポートがありません: {pattern}")
                    for filename in paths:
                        path = Path(filename)
                        if path.stat().st_mtime_ns < started_ns:
                            raise ValueError(f"今回の検査より古いテストレポートです: {path}")
                        cases.extend(reader(path))
                if len(cases) < args.minimum_test_cases or not all(passed for _, passed in cases):
                    raise ValueError(f"必須テストが不足・失敗・skip: {len(cases)} 件")
            except (OSError, ValueError, KeyError, ET.ParseError) as exception:
                code = code or 1
                log.write(f"\n{exception}\n")
                print(exception, file=sys.stderr)
    write_json(directory / "sarif" / f"{args.id}.sarif", {
        "version": "2.1.0", "runs": [{"tool": {"driver": {"name": args.tool}},
            "invocations": [{"executionSuccessful": code == 0, "exitCode": code}],
            "results": [] if code == 0 else [{"ruleId": f"{args.id}.execution", "level": "error",
                "message": {"text": f"検査失敗 (exit={code})。ログ: {log_path}"}}]}]})
    return code if code >= 0 else 1


if __name__ == "__main__":
    sys.exit(main())
