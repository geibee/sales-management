#!/usr/bin/env python3
"""`claude --output-format stream-json` の NDJSON を人間に読める進捗行に変換する。

stdin から 1 行 1 JSON イベントを読み、tool 呼び出し / assistant text / final result
を要点だけ stdout に出す。stream-json の生 JSON は ralph.sh 側で tee により
SESSION_OUT に丸ごと保存されるので、後続の cost 抽出はそちらを使う。

入力イベントの主な type:
  - system  / subtype=init           — セッション初期化
  - user    / content=tool_result    — 直前 tool の結果
  - assistant / content=text|tool_use — 思考と道具の使用
  - result  / subtype=success|error  — 最終結果と total_cost_usd
"""
from __future__ import annotations

import json
import sys
from datetime import datetime


def short(text: str | None, n: int = 100) -> str:
    if not text:
        return ""
    s = text.replace("\n", " ").strip()
    return s if len(s) <= n else s[: n - 1] + "…"


def render_tool_use(name: str, inp: dict) -> str:
    if name in ("Read", "Edit", "Write"):
        path = inp.get("file_path") or inp.get("path") or ""
        return f"[{name}] {path}"
    if name == "Bash":
        return f"[Bash] {short(inp.get('command'), 120)}"
    if name in ("Grep", "Glob"):
        return f"[{name}] {short(inp.get('pattern') or inp.get('path'), 100)}"
    if name == "TodoWrite":
        todos = inp.get("todos") or []
        return f"[TodoWrite] {len(todos)} item(s)"
    if name == "Agent":
        desc = inp.get("description") or inp.get("subagent_type") or ""
        return f"[Agent] {short(desc, 80)}"
    return f"[{name}] {short(json.dumps(inp, ensure_ascii=False), 100)}"


def render(event: dict) -> str | None:
    et = event.get("type")
    if et == "system":
        sub = event.get("subtype", "")
        sid = event.get("session_id", "")
        return f"[init {sub}] session={sid[:12]}" if sid else f"[init {sub}]"
    if et == "user":
        msg = event.get("message", {}) or {}
        content = msg.get("content")
        if isinstance(content, list):
            for c in content:
                if isinstance(c, dict) and c.get("type") == "tool_result":
                    rc = c.get("content")
                    if isinstance(rc, list):
                        for cc in rc:
                            if isinstance(cc, dict) and cc.get("type") == "text":
                                return f"[tool_result] {short(cc.get('text'), 120)}"
                    return f"[tool_result] {short(str(rc), 120)}"
        if isinstance(content, str):
            return f"[user] {short(content)}"
        return None
    if et == "assistant":
        msg = event.get("message", {}) or {}
        out: list[str] = []
        for c in msg.get("content", []) or []:
            if c.get("type") == "text":
                txt = short(c.get("text"))
                if txt:
                    out.append(f"[assistant] {txt}")
            elif c.get("type") == "tool_use":
                out.append(render_tool_use(c.get("name", "?"), c.get("input", {})))
        return "\n  ".join(out) if out else None
    if et == "result":
        sub = event.get("subtype", "")
        cost = event.get("total_cost_usd", 0)
        ok = "error" if event.get("is_error") else "ok"
        return f"[done {sub} {ok}] cost=${cost}"
    return None


def main() -> int:
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        try:
            event = json.loads(line)
        except json.JSONDecodeError:
            continue
        rendered = render(event)
        if rendered:
            ts = datetime.now().strftime("%H:%M:%S")
            print(f"{ts} {rendered}", flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
