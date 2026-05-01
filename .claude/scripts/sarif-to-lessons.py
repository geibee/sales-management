#!/usr/bin/env python3
"""Stop フック: merged.sarif を読み、頻出 ruleId を AGENTS.md に「教訓」として追記する。

挙動:
  1. apps/api-fsharp/ci-results/merged.sarif (CWD 相対) を読む。なければ exit 0
  2. ruleId を `<tool_name>.<ruleId>` でカウントし、最初に出会った message を保持
  3. 環境変数 SARIF_LESSONS_THRESHOLD (default=3) 以上の検出回数の rule を教訓として整形
  4. root の AGENTS.md を開き、`## 失敗から学んだこと (自動生成)` 見出しを末尾に追加 (なければ)
  5. 同じ ruleId の行が既に教訓セクションにあればスキップ。新規行のみ append

CWD は Claude Code 起動時のリポジトリルートを想定。
"""
import json
import os
import pathlib
import sys
from collections import Counter
from datetime import date

THRESHOLD = int(os.environ.get("SARIF_LESSONS_THRESHOLD", "3"))
HEADING = "## 失敗から学んだこと (自動生成)"
HEADING_ANCHOR = "\n" + HEADING  # 行頭の見出しのみマッチさせる (本文中の引用と区別)
SARIF_PATH = pathlib.Path("apps/api-fsharp/ci-results/merged.sarif")
AGENTS_PATH = pathlib.Path("AGENTS.md")


def load_results() -> tuple[Counter, dict[str, str]]:
    counts: Counter = Counter()
    samples: dict[str, str] = {}
    if not SARIF_PATH.exists():
        return counts, samples
    try:
        data = json.loads(SARIF_PATH.read_text())
    except json.JSONDecodeError:
        return counts, samples
    for run in data.get("runs", []) or []:
        tool = ((run.get("tool") or {}).get("driver") or {}).get("name", "?")
        for r in run.get("results", []) or []:
            rid = r.get("ruleId") or "?"
            key = f"{tool}.{rid}"
            counts[key] += 1
            if key not in samples:
                msg = ((r.get("message") or {}).get("text") or "")
                samples[key] = msg.replace("\n", " ").strip()[:120]
    return counts, samples


def ensure_section(text: str) -> str:
    if HEADING_ANCHOR in text:
        return text
    suffix = "\n\n" if not text.endswith("\n") else "\n"
    return text + suffix + HEADING + "\n"


def append_lessons(text: str, new_lines: list[str]) -> str:
    if not new_lines:
        return text
    text = ensure_section(text)
    idx = text.find(HEADING_ANCHOR)
    head = text[: idx + 1]  # 改行込みの直前まで
    tail = text[idx + 1 :]  # HEADING を含む後半
    return head + tail.rstrip("\n") + "\n" + "\n".join(new_lines) + "\n"


def main() -> int:
    if not AGENTS_PATH.exists():
        return 0
    counts, samples = load_results()
    if not counts:
        return 0

    today = date.today().isoformat()
    text = AGENTS_PATH.read_text()
    text = ensure_section(text)
    section_text = text[text.find(HEADING_ANCHOR):]

    new_lines: list[str] = []
    for key, n in counts.most_common():
        if n < THRESHOLD:
            continue
        if key in section_text:
            continue
        msg = samples.get(key, "")
        new_lines.append(f"- {today} {key}: {n}回検出。{msg}")

    if not new_lines:
        return 0

    text = append_lessons(text, new_lines)
    AGENTS_PATH.write_text(text)
    print(f"[sarif-to-lessons] appended {len(new_lines)} lesson(s) to {AGENTS_PATH}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
