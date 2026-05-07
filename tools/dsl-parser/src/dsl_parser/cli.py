"""CLI entrypoint for the DSL parser.

Usage::

    dsl-parser <path-to-dsl-file>
    dsl-parser -            # read from stdin

Outputs the AST as JSON on stdout.
"""

from __future__ import annotations

import json
import sys

from parsimonious.exceptions import ParseError

from dsl_parser.parser import parse


def main() -> None:
    if len(sys.argv) != 2:
        print("usage: dsl-parser <path-or-->", file=sys.stderr)
        sys.exit(2)

    arg = sys.argv[1]
    source = sys.stdin.read() if arg == "-" else open(arg, encoding="utf-8").read()

    try:
        ast = parse(source)
    except ParseError as e:
        print(f"parse error: {e}", file=sys.stderr)
        sys.exit(1)

    json.dump(ast.to_dict(), sys.stdout, ensure_ascii=False, indent=2)
    sys.stdout.write("\n")
