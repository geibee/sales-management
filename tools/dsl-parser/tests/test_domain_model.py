"""Smoke test: ensure the full dsl/domain-model.md is parseable.

This test is brittle to changes in the DSL by design. It guards against
silent grammar regressions when domain-model.md evolves.
"""

from __future__ import annotations

from pathlib import Path

from dsl_parser import parse
from dsl_parser.cli import extract_dsl_from_markdown


REPO_ROOT = Path(__file__).resolve().parents[3]
DOMAIN_MODEL = REPO_ROOT / "dsl" / "domain-model.md"


def test_domain_model_parses() -> None:
    raw = DOMAIN_MODEL.read_text(encoding="utf-8")
    source = extract_dsl_from_markdown(raw)
    program = parse(source)

    decls = program.declarations
    assert len(decls) > 0, "expected at least one declaration"

    # Confirm both DataDecl and BehaviorDecl are present (sanity check
    # that the [CORE] grammar is exercising both branches).
    type_names = {type(d).__name__ for d in decls}
    assert "DataDecl" in type_names
    assert "BehaviorDecl" in type_names
