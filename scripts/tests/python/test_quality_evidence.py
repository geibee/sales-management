import runpy
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
SCRIPT = ROOT / "scripts/quality-evidence.py"


def test_identical_coverage_copies_are_counted_once(tmp_path):
    deduplicate_reports = runpy.run_path(str(SCRIPT))["deduplicate_reports"]
    first = tmp_path / "first.xml"
    second = tmp_path / "nested" / "second.xml"
    second.parent.mkdir()
    first.write_text("<coverage line-rate='0.9'/>")
    second.write_text(first.read_text())

    assert deduplicate_reports([first, second]) == [first]


def test_distinct_coverage_reports_remain_distinct(tmp_path):
    deduplicate_reports = runpy.run_path(str(SCRIPT))["deduplicate_reports"]
    first = tmp_path / "first.xml"
    second = tmp_path / "second.xml"
    first.write_text("<coverage line-rate='0.9'/>")
    second.write_text("<coverage line-rate='0.8'/>")

    assert deduplicate_reports([first, second]) == [first, second]
