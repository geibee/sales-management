#!/usr/bin/env python3
"""既存 CLI の互換入口。SARIF 監査はリポジトリ共通実装へ委譲する。"""

from pathlib import Path
import runpy


def main() -> None:
    runpy.run_path(str(Path(__file__).resolve().parents[3] / "scripts/sarif-audit.py"))["main"]()


if __name__ == "__main__":
    main()
