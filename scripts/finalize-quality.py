#!/usr/bin/env python3
"""workflow の後段で生成される CodeQL を同じ実行へ取り込み、全量を監査する。"""

import argparse
import json
from pathlib import Path
import shutil
import subprocess
import sys


ROOT = Path(__file__).resolve().parents[1]


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--target", choices=("fsharp", "spring"), required=True)
    parser.add_argument("--codeql-directory", type=Path)
    args = parser.parse_args()
    directory = Path(json.loads((ROOT / f"ci-results/latest-{args.target}.json").read_text())["directory"])
    if args.codeql_directory:
        reports = list(args.codeql_directory.glob("**/*.sarif"))
        if len(reports) == 1:
            shutil.copy2(reports[0], directory / "sarif/codeql.sarif")
        else:
            print(f"CodeQL SARIF は1件必須です: {len(reports)}", file=sys.stderr)
    return subprocess.run([sys.executable, str(ROOT / "scripts/quality-evidence.py"), "--target", args.target,
                           "--directory", str(directory), "--profile", "full"], check=False).returncode


if __name__ == "__main__":
    sys.exit(main())
