#!/usr/bin/env python3
"""マルチエージェント・オーケストレーター。

`prd.md` の `^- \\[ \\]` 行を 1 タスクとして取り出し、4 専門エージェント
(domain-modeler → test-writer → refactorer → doc-updater) に順次投げる。
最後に `apps/api-fsharp/ci.sh` を実行し、緑なら master.py 側で `[x]` に書き換える。

Step 30 (`harness/ralph.sh`) は 1 タスクごとに本スクリプトを呼ぶ薄いランナーになる。

# 使い方

    python3 .harness/master.py --prd prd.md            # 実エージェント実行 (Claude CLI 起動)
    python3 .harness/master.py --prd prd.md --dry-run  # inbox メッセージ生成だけ確認

# 環境変数

    CLAUDE_CODE_BIN  Claude Code 実行パス (default: claude)
    SUBPROCESS_TIMEOUT_SEC  各エージェント呼び出しの timeout (default: 600)
"""
from __future__ import annotations

import argparse
import json
import os
import pathlib
import re
import subprocess
import sys
import time
import uuid

ROOT = pathlib.Path(__file__).resolve().parent.parent
HARNESS = ROOT / ".harness"
INBOX = HARNESS / "inbox"
OUTBOX = HARNESS / "outbox"

AGENT_PIPELINE = ["domain-modeler", "test-writer", "refactorer", "doc-updater"]

CLAUDE_BIN = os.environ.get("CLAUDE_CODE_BIN", "claude")
TIMEOUT = int(os.environ.get("SUBPROCESS_TIMEOUT_SEC", "600"))


def decompose(prd_path: pathlib.Path) -> list[str]:
    """PRD から `^- \\[ \\] <task>` 行のタスクテキストを抽出する。"""
    if not prd_path.exists():
        return []
    tasks: list[str] = []
    for line in prd_path.read_text().splitlines():
        m = re.match(r"^\s*-\s*\[\s\]\s+(.+)$", line)
        if m:
            tasks.append(m.group(1).strip())
    return tasks


def write_inbox(agent: str, task_id: str, task_text: str) -> pathlib.Path:
    target = INBOX / agent / f"{task_id}.json"
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(json.dumps({"task": task_text, "task_id": task_id}, ensure_ascii=False, indent=2))
    return target


def call_agent(agent: str, inbox_msg: pathlib.Path, trace_id: str | None) -> int:
    """Claude Code CLI でエージェントを起動する。

    Claude Code CLI 仕様 (`claude --help` で確認):
      - 非対話 + JSON + budget は `--print` 必須
      - `--agent <name>` で agent 切替 (`.claude/agents/` または `.harness/agents/` 等を解決)
      - prompt は positional argument
      - resume はデフォルト OFF (明示的に `--resume` しなければフレッシュ)
    """
    msg_text = inbox_msg.read_text()
    cmd = [
        CLAUDE_BIN,
        "--print",
        "--no-session-persistence",
        "--permission-mode", "bypassPermissions",
        "--agent", agent,
        "--output-format", "json",
        msg_text,
    ]
    env = dict(os.environ)
    if trace_id:
        env["CLAUDE_TRACE_ID"] = trace_id
    print(f"[master] launching agent={agent} (prompt={inbox_msg})")
    try:
        result = subprocess.run(cmd, env=env, timeout=TIMEOUT, check=False)
        return result.returncode
    except FileNotFoundError:
        print(f"[master] {CLAUDE_BIN} not found in PATH; treat as dry-run", file=sys.stderr)
        return -1
    except subprocess.TimeoutExpired:
        print(f"[master] agent={agent} timed out (>{TIMEOUT}s)", file=sys.stderr)
        return -2


def run_ci() -> int:
    ci = ROOT / "apps" / "api-fsharp" / "ci.sh"
    if not ci.exists():
        print(f"[master] {ci} not found; skipping CI", file=sys.stderr)
        return 0
    print(f"[master] running CI: {ci}")
    result = subprocess.run([str(ci)], cwd=str(ci.parent), check=False)
    return result.returncode


def process_task(task_text: str, *, dry_run: bool, trace_id: str | None) -> int:
    task_id = uuid.uuid4().hex[:8]
    print(f"[master] task_id={task_id} task={task_text!r}")
    for agent in AGENT_PIPELINE:
        msg = write_inbox(agent, task_id, task_text)
        if dry_run:
            print(f"[master:dry-run] wrote {msg} (skipping agent invocation)")
            continue
        rc = call_agent(agent, msg, trace_id)
        if rc != 0:
            print(f"[master] agent={agent} failed (rc={rc}); aborting task", file=sys.stderr)
            return rc
    if dry_run:
        return 0
    return run_ci()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--prd", default="prd.md", help="PRD ファイル (default: prd.md)")
    parser.add_argument("--dry-run", action="store_true", help="エージェント呼び出しせず inbox 書き込みのみ")
    parser.add_argument("--max-tasks", type=int, default=1, help="今回処理するタスク数 (default: 1)")
    args = parser.parse_args()

    prd_path = ROOT / args.prd
    tasks = decompose(prd_path)
    if not tasks:
        print(f"[master] no '- [ ]' tasks in {prd_path}", file=sys.stderr)
        return 0

    trace_id = uuid.uuid4().hex
    print(f"[master] trace_id={trace_id} pending={len(tasks)} mode={'dry-run' if args.dry_run else 'live'}")

    processed = 0
    for task_text in tasks[: args.max_tasks]:
        rc = process_task(task_text, dry_run=args.dry_run, trace_id=trace_id)
        if rc != 0:
            return rc
        processed += 1
    print(f"[master] processed={processed}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
