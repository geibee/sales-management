import json
import subprocess
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
SCRIPT = ROOT / "scripts/quality-run.py"


def test_failed_command_still_has_log_sarif_and_exit_status(tmp_path):
    directory = tmp_path / "run"
    directory.mkdir()
    result = subprocess.run([sys.executable, str(SCRIPT), "step", "--directory", str(directory),
                             "--id", "compile", "--tool", "Compiler", "--", sys.executable,
                             "-c", "print('intentional failure'); raise SystemExit(7)"], check=False)
    assert result.returncode == 7
    report = json.loads((directory / "sarif/compile.sarif").read_text())
    assert report["runs"][0]["invocations"][0]["executionSuccessful"] is False
    assert report["runs"][0]["results"][0]["level"] == "error"
    assert "intentional failure" in (directory / "logs/compile.log").read_text()


def test_missing_command_is_failure_evidence(tmp_path):
    result = subprocess.run([sys.executable, str(SCRIPT), "step", "--directory", str(tmp_path),
                             "--id", "missing", "--tool", "Missing", "--", "no-such-quality-tool"], check=False)
    assert result.returncode != 0
    assert json.loads((tmp_path / "sarif/missing.sarif").read_text())["runs"][0]["results"]


def test_successful_command_without_test_report_is_rejected(tmp_path):
    result = subprocess.run([sys.executable, str(SCRIPT), "step", "--directory", str(tmp_path),
        "--id", "pact", "--tool", "Pact provider", "--test-report", str(tmp_path / "missing.xml"),
        "--minimum-test-cases", "2", "--", sys.executable, "-c", "pass"], check=False)
    assert result.returncode == 1
    assert json.loads((tmp_path / "sarif/pact.sarif").read_text())["runs"][0]["results"]


def test_successful_command_with_skipped_test_is_rejected(tmp_path):
    report = tmp_path / "pact.xml"
    xml = "<testsuite><testcase classname='Pact' name='verify'><skipped/></testcase></testsuite>"
    code = f"from pathlib import Path; Path({str(report)!r}).write_text({xml!r})"
    result = subprocess.run([sys.executable, str(SCRIPT), "step", "--directory", str(tmp_path),
        "--id", "pact", "--tool", "Pact provider", "--test-report", str(report),
        "--minimum-test-cases", "1", "--", sys.executable, "-c", code], check=False)
    assert result.returncode == 1
    assert json.loads((tmp_path / "sarif/pact.sarif").read_text())["runs"][0]["results"]
