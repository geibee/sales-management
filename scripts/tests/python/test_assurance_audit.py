import json

import pytest


def write_junit(path, cases):
    body = "".join(
        f'<testcase classname="{classname}" name="{name}">{child}</testcase>'
        for classname, name, child in cases
    )
    path.write_text(f'<testsuite tests="{len(cases)}">{body}</testsuite>', encoding="utf-8")


def write_catalog(path):
    path.write_text(
        json.dumps(
            {
                "version": 1,
                "targets": ["fsharp", "spring"],
                "guarantees": [
                    {
                        "id": "G-BEHAVIOR-001",
                        "purpose": "同じ業務保証を両実装で検査する",
                        "requiredTargets": ["fsharp", "spring"],
                        "evidence": {
                            "fsharp": [
                                {"type": "test", "selector": "FsharpTests#works"}
                            ],
                            "spring": [
                                {"type": "test", "selector": "SpringTests#works"}
                            ],
                        },
                    },
                    {
                        "id": "G-SECURITY-001",
                        "purpose": "静的検査が必須である",
                        "requiredTargets": ["fsharp", "spring"],
                        "evidence": {
                            "fsharp": [{"type": "sarif", "toolId": "source-rules"}],
                            "spring": [{"type": "sarif", "toolId": "codeql"}],
                        },
                    },
                ],
                "metrics": [
                    {
                        "id": "lineCoverage",
                        "minimum": {"fsharp": 88.2, "spring": 88.2},
                    }
                ],
            },
            ensure_ascii=False,
        ),
        encoding="utf-8",
    )


def run_audit(mod, tmp_path, set_argv, *, target="fsharp", cases=None, metrics=None):
    catalog = tmp_path / "catalog.json"
    write_catalog(catalog)
    junit = tmp_path / "results.xml"
    write_junit(junit, cases or [("FsharpTests", "works", "")])
    metric_path = tmp_path / "metrics.json"
    metric_path.write_text(
        json.dumps(metrics or {"lineCoverage": 88.2}), encoding="utf-8"
    )
    sarif_manifest = tmp_path / "sarif-manifest.json"
    sarif_manifest.write_text(
        json.dumps(
            {
                "version": 1,
                "tools": [
                    {"id": "source-rules", "errors": 0, "skipped": False},
                    {"id": "codeql", "errors": 0, "skipped": False},
                ],
            }
        ),
        encoding="utf-8",
    )
    manifest = tmp_path / "assurance-manifest.json"
    sarif = tmp_path / "assurance.sarif"
    set_argv(
        "--catalog",
        str(catalog),
        "--target",
        target,
        "--test-report",
        str(junit),
        "--metrics",
        str(metric_path),
        "--sarif-manifest",
        str(sarif_manifest),
        "--manifest",
        str(manifest),
        "--sarif-output",
        str(sarif),
    )
    mod.main()
    return json.loads(manifest.read_text()), json.loads(sarif.read_text())


def test_successful_runtime_evidence_and_metric_are_recorded(load_script, tmp_path, set_argv):
    mod = load_script("scripts/assurance-audit.py")

    manifest, sarif = run_audit(mod, tmp_path, set_argv)

    assert manifest["target"] == "fsharp"
    assert manifest["summary"] == {"passed": 3, "failed": 0}
    assert {item["id"] for item in manifest["guarantees"]} == {
        "G-BEHAVIOR-001",
        "G-SECURITY-001",
    }
    assert sarif["version"] == "2.1.0"
    assert sarif["runs"][0]["results"] == []


@pytest.mark.parametrize(
    ("cases", "metrics"),
    [
        ([("OtherTests", "works", "")], {"lineCoverage": 88.2}),
        ([("FsharpTests", "works", "<skipped/>")], {"lineCoverage": 88.2}),
        ([("FsharpTests", "works", "")], {"lineCoverage": 88.19}),
    ],
)
def test_missing_skipped_or_below_floor_evidence_fails_closed(
    load_script, tmp_path, set_argv, cases, metrics
):
    mod = load_script("scripts/assurance-audit.py")

    with pytest.raises(SystemExit):
        run_audit(mod, tmp_path, set_argv, cases=cases, metrics=metrics)


def test_file_presence_is_not_accepted_as_evidence(load_script, tmp_path, set_argv):
    mod = load_script("scripts/assurance-audit.py")
    catalog = tmp_path / "catalog.json"
    write_catalog(catalog)
    document = json.loads(catalog.read_text())
    document["guarantees"][0]["evidence"]["fsharp"] = [
        {"type": "file", "path": "SomeTest.fs"}
    ]
    catalog.write_text(json.dumps(document), encoding="utf-8")
    set_argv(
        "--catalog",
        str(catalog),
        "--target",
        "fsharp",
        "--manifest",
        str(tmp_path / "manifest.json"),
        "--sarif-output",
        str(tmp_path / "assurance.sarif"),
    )

    with pytest.raises(SystemExit):
        mod.main()


def test_catalog_requires_evidence_for_every_required_target(load_script, tmp_path, set_argv):
    mod = load_script("scripts/assurance-audit.py")
    catalog = tmp_path / "catalog.json"
    write_catalog(catalog)
    document = json.loads(catalog.read_text())
    del document["guarantees"][0]["evidence"]["spring"]
    catalog.write_text(json.dumps(document), encoding="utf-8")
    set_argv(
        "--catalog",
        str(catalog),
        "--target",
        "fsharp",
        "--manifest",
        str(tmp_path / "manifest.json"),
        "--sarif-output",
        str(tmp_path / "assurance.sarif"),
    )

    with pytest.raises(SystemExit):
        mod.main()


@pytest.mark.parametrize("child", ["<failure/>", "<error/>", "<skipped/>"])
def test_a_passing_duplicate_does_not_hide_an_unsuccessful_case(load_script, tmp_path, set_argv, child):
    mod = load_script("scripts/assurance-audit.py")
    with pytest.raises(SystemExit):
        run_audit(mod, tmp_path, set_argv, cases=[
            ("FsharpTests", "works", ""), ("FsharpTests", "works", child),
        ])


@pytest.mark.parametrize("value", [float("nan"), float("inf"), -1, "88.2", True])
def test_invalid_metric_is_not_success(load_script, tmp_path, set_argv, value):
    mod = load_script("scripts/assurance-audit.py")
    with pytest.raises(SystemExit):
        run_audit(mod, tmp_path, set_argv, metrics={"lineCoverage": value})


def test_trx_uses_test_definition_identity_and_outcome(load_script, tmp_path):
    mod = load_script("scripts/assurance-audit.py")
    report = tmp_path / "tests.trx"
    report.write_text('''<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
      <TestDefinitions><UnitTest id="a"><TestMethod className="Domain.Tests" name="keeps values"/></UnitTest></TestDefinitions>
      <Results><UnitTestResult testId="a" testName="display" outcome="Passed"/>
      <UnitTestResult testId="a" testName="display2" outcome="NotExecuted"/></Results>
    </TestRun>''')
    assert mod.read_tests(report) == [("Domain.Tests#keeps values", True), ("Domain.Tests#keeps values", False)]


def test_trx_falls_back_to_unit_test_name_when_adapter_omits_method_name(load_script, tmp_path):
    mod = load_script("scripts/assurance-audit.py")
    report = tmp_path / "tests.trx"
    report.write_text('''<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
      <TestDefinitions><UnitTest id="a" name="Domain.Tests.generated case">
        <TestMethod className="Domain.Tests.generated case"/>
      </UnitTest></TestDefinitions>
      <Results><UnitTestResult testId="a" outcome="Passed"/></Results>
    </TestRun>''')

    assert mod.read_tests(report) == [("Domain.Tests.generated case", True)]


def test_current_run_rejects_old_evidence(load_script, tmp_path):
    mod = load_script("scripts/assurance-audit.py")
    report = tmp_path / "old.xml"
    report.write_text("old")
    with pytest.raises(ValueError, match="実行開始"):
        mod.check_freshness(report, {"startedNs": report.stat().st_mtime_ns + 1})
