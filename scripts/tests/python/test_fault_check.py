import pytest


def test_fault_requires_exactly_one_source_match(load_script, tmp_path):
    mod = load_script("scripts/fault-check.py")
    source = tmp_path / "source.fs"
    source.write_text("let value = 1\nlet value = 1\n")
    with pytest.raises(ValueError):
        mod.inject(tmp_path, {"file": "source.fs", "before": "let value = 1", "after": "let value = 2"})
    assert source.read_text().count("= 1") == 2


def test_compile_failure_and_missing_reports_are_not_killed(load_script, tmp_path):
    mod = load_script("scripts/fault-check.py")
    assert not mod.detected([], "compile failed")
    assert not mod.detected([("expected#test", True)], "AssertionError")
    assert not mod.detected([("expected#test", False)], "Docker is unavailable")
    assert mod.detected([("expected#test", False)], "Xunit.Sdk.EqualException")
