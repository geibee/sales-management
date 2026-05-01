#!/usr/bin/env python3
"""PostToolUse フック: stdin の JSON から OTLP/HTTP スパンを組み立て Jaeger に POST する。

入力 (Claude Code の hook 仕様に準拠):
    {
      "tool_name": "Read",
      "tool_input": { ... },
      "tool_response": { ... },
      ...
    }

trace_id は ~/.claude-trace-id から取得 (start-trace.py が SessionStart で書いた値)。
Jaeger 不在時は静かに print して exit 0 (Claude Code セッションをブロックしない)。
"""
import json
import os
import pathlib
import secrets
import sys
import time
import urllib.error
import urllib.request


OTLP_ENDPOINT = os.environ.get("OTLP_ENDPOINT", "http://localhost:4318/v1/traces")
SERVICE_NAME = os.environ.get("OTEL_SERVICE_NAME", "claude-agent-harness")


def read_trace_id() -> str:
    target = pathlib.Path.home() / ".claude-trace-id"
    if target.exists():
        return target.read_text().strip()
    # SessionStart フックが走らなかった場合は 1 回限りの新規 trace
    return secrets.token_hex(16)


def build_payload(trace_id: str, hook_input: dict) -> dict:
    span_id = secrets.token_hex(8)
    end_ns = int(time.time_ns())
    duration_ns = int(float(hook_input.get("duration_ms") or 0) * 1_000_000) or 1_000_000
    start_ns = end_ns - duration_ns
    tool_name = hook_input.get("tool_name") or "unknown"
    exit_code = hook_input.get("exit_code")
    status = {"code": 2 if (isinstance(exit_code, int) and exit_code != 0) else 1}

    return {
        "resourceSpans": [{
            "resource": {
                "attributes": [
                    {"key": "service.name", "value": {"stringValue": SERVICE_NAME}},
                ]
            },
            "scopeSpans": [{
                "scope": {"name": "claude-code.hook"},
                "spans": [{
                    "traceId": trace_id,
                    "spanId": span_id,
                    "name": f"tool:{tool_name}",
                    "kind": 1,  # INTERNAL
                    "startTimeUnixNano": str(start_ns),
                    "endTimeUnixNano": str(end_ns),
                    "status": status,
                    "attributes": [
                        {"key": "tool.name", "value": {"stringValue": str(tool_name)}},
                        {"key": "tool.exit_code", "value": {"intValue": str(exit_code)}}
                            if exit_code is not None else
                            {"key": "tool.exit_code", "value": {"stringValue": "n/a"}},
                    ],
                }],
            }],
        }],
    }


def main() -> int:
    raw = sys.stdin.read().strip()
    if not raw:
        return 0
    try:
        hook_input = json.loads(raw)
    except json.JSONDecodeError:
        print(f"[emit-otel] non-JSON stdin ignored: {raw[:80]}", file=sys.stderr)
        return 0

    trace_id = read_trace_id()
    payload = build_payload(trace_id, hook_input)

    try:
        req = urllib.request.Request(
            OTLP_ENDPOINT,
            data=json.dumps(payload).encode("utf-8"),
            headers={"Content-Type": "application/json"},
        )
        with urllib.request.urlopen(req, timeout=2) as resp:
            resp.read()
    except (urllib.error.URLError, OSError, TimeoutError) as e:
        # Jaeger 不在やネットワーク不通時は黙って通す
        print(f"[emit-otel] OTLP send failed (ignored): {e}", file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
