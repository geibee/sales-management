import hashlib
import json

import pytest


def test_mutation_score_counts_only_killed_mutations(load_script, tmp_path, monkeypatch):
    mod = load_script("apps/api-spring/scripts/verify-quality-ratchets.py")
    monkeypatch.setattr(mod, "ROOT", tmp_path)
    report = tmp_path / "domain/target/pit-reports/mutations.xml"
    report.parent.mkdir(parents=True)
    report.write_text(
        """<mutations>
        <mutation status="KILLED"/><mutation status="SURVIVED"/>
        <mutation status="NO_COVERAGE"/><mutation status="KILLED"/>
        </mutations>"""
    )

    assert mod.mutation_score() == 50.0


def test_mutation_report_missing_is_fail_closed(load_script, tmp_path, monkeypatch):
    mod = load_script("apps/api-spring/scripts/verify-quality-ratchets.py")
    monkeypatch.setattr(mod, "ROOT", tmp_path)

    with pytest.raises(SystemExit):
        mod.mutation_score()


def sarif(tool: str, *, level: str | None = None, skipped: bool = False) -> dict:
    result = [] if level is None else [{"ruleId": "fixture", "level": level}]
    return {
        "version": "2.1.0",
        "runs": [
            {
                "tool": {"driver": {"name": tool}},
                "properties": {"skipped": skipped},
                "invocations": [{"executionSuccessful": True}],
                "results": result,
            }
        ],
    }


def run_audit(mod, tmp_path, set_argv, document: dict):
    sarif_dir = tmp_path / "sarif"
    sarif_dir.mkdir()
    report = sarif_dir / "fixture.sarif"
    report.write_text(json.dumps(document))
    spec = tmp_path / "tools.json"
    spec.write_text(json.dumps({"tools": [{"id": "fixture", "tool": "Fixture", "file": report.name}]}))
    manifest = tmp_path / "manifest.json"
    merged = tmp_path / "merged.sarif"
    set_argv(
        "--spec",
        str(spec),
        "--sarif-dir",
        str(sarif_dir),
        "--manifest",
        str(manifest),
        "--merged",
        str(merged),
    )
    mod.main()
    return report, manifest, merged


def test_sarif_audit_records_hash_and_merges_valid_report(load_script, tmp_path, set_argv):
    mod = load_script("apps/api-spring/scripts/sarif-audit.py")
    report, manifest, merged = run_audit(mod, tmp_path, set_argv, sarif("Fixture"))

    entry = json.loads(manifest.read_text())["tools"][0]
    assert entry["sha256"] == hashlib.sha256(report.read_bytes()).hexdigest()
    assert entry["results"] == 0
    assert len(json.loads(merged.read_text())["runs"]) == 1


@pytest.mark.parametrize("document", [sarif("Fixture", level="error"), sarif("Fixture", skipped=True)])
def test_sarif_audit_rejects_errors_and_skips(load_script, tmp_path, set_argv, document):
    mod = load_script("apps/api-spring/scripts/sarif-audit.py")

    with pytest.raises(SystemExit):
        run_audit(mod, tmp_path, set_argv, document)
