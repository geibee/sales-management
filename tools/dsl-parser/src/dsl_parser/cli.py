"""CLI entrypoint for the DSL parser.

Usage::

    dsl-parser <path-to-input>
    dsl-parser --format spec <path-to-input>
    dsl-parser -                    # read from stdin

If the input path ends with ``.md``, the first fenced code block (```...```)
is extracted and parsed. Otherwise the whole file content is parsed.

Outputs the AST as JSON on stdout by default.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

from parsimonious.exceptions import ParseError

from dsl_parser.parser import parse
from dsl_parser.spec import to_spec_dict


_CODE_BLOCK_RE = re.compile(r"^```[^\n]*\n(.*?)\n```", re.DOTALL | re.MULTILINE)


def extract_dsl_from_markdown(text: str) -> str:
    """Return the first fenced code block content, or raise ValueError."""
    match = _CODE_BLOCK_RE.search(text)
    if not match:
        raise ValueError("no fenced code block found in markdown input")
    return match.group(1)


def main() -> None:
    parser = argparse.ArgumentParser(description="Sales Management DSL parser")
    parser.add_argument(
        "--format",
        choices=("ast", "spec"),
        default="ast",
        help="出力形式。既定は既存互換の ast。",
    )
    parser.add_argument("path", help="入力DSLファイル、Markdownファイル、または -")
    args = parser.parse_args()

    arg = args.path
    if arg == "-":
        raw = sys.stdin.read()
        is_markdown = False
    else:
        path = Path(arg)
        raw = path.read_text(encoding="utf-8")
        is_markdown = path.suffix == ".md"

    source = extract_dsl_from_markdown(raw) if is_markdown else raw

    try:
        ast = parse(source)
    except ParseError as e:
        print(f"parse error: {e}", file=sys.stderr)
        sys.exit(1)

    output = ast.to_dict() if args.format == "ast" else to_spec_dict(ast)
    json.dump(output, sys.stdout, ensure_ascii=False, indent=2)
    sys.stdout.write("\n")
