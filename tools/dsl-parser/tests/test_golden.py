"""Golden tests: parse each input.dsl and compare AST against expected.json."""

from __future__ import annotations

import json
from pathlib import Path

import pytest

from dsl_parser import parse


GOLDEN_DIR = Path(__file__).parent / "golden"


def _golden_cases() -> list[Path]:
    return sorted(p for p in GOLDEN_DIR.iterdir() if p.is_dir())


@pytest.mark.parametrize("case_dir", _golden_cases(), ids=lambda p: p.name)
def test_golden(case_dir: Path) -> None:
    source = (case_dir / "input.dsl").read_text(encoding="utf-8")
    expected = json.loads((case_dir / "expected.json").read_text(encoding="utf-8"))
    actual = parse(source).to_dict()
    assert actual == expected
