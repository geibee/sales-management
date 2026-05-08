"""spec.json 正規化形式のゴールデンテスト。"""

from __future__ import annotations

import json
from pathlib import Path

import pytest

from dsl_parser import parse
from dsl_parser.spec import to_spec_dict
from dsl_parser.cli import extract_dsl_from_markdown


SPEC_GOLDEN_DIR = Path(__file__).parent / "spec_golden"
REPO_ROOT = Path(__file__).resolve().parents[3]
DOMAIN_MODEL = REPO_ROOT / "dsl" / "domain-model.md"


def _spec_golden_cases() -> list[Path]:
    return sorted(p for p in SPEC_GOLDEN_DIR.iterdir() if p.is_dir())


@pytest.mark.parametrize("case_dir", _spec_golden_cases(), ids=lambda p: p.name)
def test_spec_golden(case_dir: Path) -> None:
    source = (case_dir / "input.dsl").read_text(encoding="utf-8")
    expected = json.loads((case_dir / "expected.json").read_text(encoding="utf-8"))
    actual = to_spec_dict(parse(source))
    assert actual == expected


def test_domain_model_generates_spec() -> None:
    raw = DOMAIN_MODEL.read_text(encoding="utf-8")
    source = extract_dsl_from_markdown(raw)
    spec = to_spec_dict(parse(source))

    data_names = [d["name"] for d in spec["data"]]
    behavior_names = [b["name"] for b in spec["behaviors"]]

    assert "在庫ロット" in data_names
    assert "製造完了を指示する" in behavior_names
    assert len(spec["data"]) > 0
    assert len(spec["behaviors"]) > 0

    # JSONとして決定論的に出力可能な構造であることを確認する。
    encoded = json.dumps(spec, ensure_ascii=False, indent=2)
    assert json.loads(encoded) == spec
