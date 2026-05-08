"""CLI entrypoint for the DSL parser.

Usage::

    dsl-parser <path-to-input>
    dsl-parser -                    # read from stdin

If the input path ends with ``.md``, the first fenced code block (```...```)
is extracted and parsed. Otherwise the whole file content is parsed.

Outputs the AST as JSON on stdout.
"""

from __future__ import annotations

import json
import re
import sys
from pathlib import Path

from parsimonious.exceptions import ParseError

from dsl_parser.parser import parse


_CODE_BLOCK_RE = re.compile(r"^```[^\n]*\n(.*?)\n```", re.DOTALL | re.MULTILINE)


def extract_dsl_from_markdown(text: str) -> str:
    """Return the first fenced code block content, or raise ValueError."""
    match = _CODE_BLOCK_RE.search(text)
    if not match:
        raise ValueError("no fenced code block found in markdown input")
    return match.group(1)


def main() -> None:
    if len(sys.argv) != 2:
        print("usage: dsl-parser <path-or-->", file=sys.stderr)
        sys.exit(2)

    arg = sys.argv[1]
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

    json.dump(ast.to_dict(), sys.stdout, ensure_ascii=False, indent=2)
    sys.stdout.write("\n")
