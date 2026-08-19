import json
import subprocess
import sys
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[3]
SPRING_ROOT = REPO_ROOT / "apps" / "api-spring"


def test_spring_nightly_accepts_codeql_action_driver_name(tmp_path: Path) -> None:
    nightly_spec = json.loads(
        (SPRING_ROOT / "nightly-tools.json").read_text(encoding="utf-8")
    )
    codeql_spec = next(item for item in nightly_spec["tools"] if item["id"] == "codeql")
    spec_path = tmp_path / "nightly-tools.json"
    sarif_dir = tmp_path / "sarif"
    sarif_dir.mkdir()

    spec_path.write_text(
        json.dumps({"tools": [codeql_spec]}), encoding="utf-8"
    )
    (sarif_dir / "codeql.sarif").write_text(
        json.dumps(
            {
                "version": "2.1.0",
                "runs": [
                    {
                        "tool": {
                            "driver": {
                                "name": "CodeQL",
                                "semanticVersion": "2.26.3",
                            }
                        },
                        "results": [],
                    }
                ],
            }
        ),
        encoding="utf-8",
    )

    result = subprocess.run(
        [
            sys.executable,
            str(SPRING_ROOT / "scripts" / "sarif-audit.py"),
            "--spec",
            str(spec_path),
            "--sarif-dir",
            str(sarif_dir),
            "--manifest",
            str(tmp_path / "manifest.json"),
            "--merged",
            str(tmp_path / "merged.sarif"),
        ],
        check=False,
        capture_output=True,
        text=True,
    )

    assert result.returncode == 0, result.stdout + result.stderr
