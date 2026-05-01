#!/usr/bin/env python3
"""SessionStart フック: ランダムな 16 byte の trace_id を生成し ~/.claude-trace-id に保存する。

PostToolUse フックの emit-otel.py がこの trace_id を読み取って OTLP スパンに付与することで、
1 セッション = 1 trace の単位で Jaeger に表示できる。
"""
import os
import pathlib
import secrets


def main() -> int:
    trace_id = secrets.token_hex(16)  # 16 bytes = 32 hex chars (W3C Trace Context 仕様)
    target = pathlib.Path.home() / ".claude-trace-id"
    target.write_text(trace_id + "\n")
    print(f"[start-trace] trace_id={trace_id}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
