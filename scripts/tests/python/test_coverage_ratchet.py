"""coverage-ratchet.py (backend カバレッジラチェット) の単体テスト。

ラチェットは全ゲートの土台なので、fail-closed 挙動 (入力欠如 = 失敗) と
EPSILON 境界を固定する (issue #9 Tier2-17)。
"""

import json

import pytest

SCRIPT = "apps/api-fsharp/scripts/coverage-ratchet.py"

COBERTURA = '<?xml version="1.0"?>\n<coverage line-rate="{line}" branch-rate="{branch}"></coverage>\n'


@pytest.fixture
def ratchet(load_script, tmp_path, monkeypatch):
    mod = load_script(SCRIPT)
    monkeypatch.setattr(mod, "baseline_path", tmp_path / "baseline.json")
    monkeypatch.delenv("RATCHET_UPDATE", raising=False)

    def _run(line: float, branch: float, baseline: dict) -> int:
        cob = tmp_path / "coverage.cobertura.xml"
        cob.write_text(COBERTURA.format(line=line / 100, branch=branch / 100))
        mod.baseline_path.write_text(json.dumps(baseline))
        import sys

        monkeypatch.setattr(sys, "argv", ["prog", str(cob)])
        return mod.main()

    _run.mod = mod
    _run.tmp = tmp_path
    return _run


def test_baseline_と同値なら合格(ratchet):
    assert ratchet(80.0, 50.0, {"line": 80.0, "branch": 50.0}) == 0


def test_EPSILON_を超えて下回ると失敗(ratchet):
    assert ratchet(79.0, 50.0, {"line": 80.0, "branch": 50.0}) == 1


def test_EPSILON_以内の揺れは許容(ratchet):
    assert ratchet(79.95, 50.0, {"line": 80.0, "branch": 50.0}) == 0


def test_branch_だけの退行でも失敗(ratchet):
    assert ratchet(80.0, 40.0, {"line": 80.0, "branch": 50.0}) == 1


def test_改善時は既定で_baseline_を書き換えない(ratchet):
    assert ratchet(90.0, 60.0, {"line": 80.0, "branch": 50.0}) == 0
    assert json.loads(ratchet.mod.baseline_path.read_text()) == {"line": 80.0, "branch": 50.0}


def test_RATCHET_UPDATE_で_baseline_を現在値へ引き上げる(ratchet, monkeypatch):
    monkeypatch.setenv("RATCHET_UPDATE", "1")
    assert ratchet(90.0, 60.0, {"line": 80.0, "branch": 50.0}) == 0
    assert json.loads(ratchet.mod.baseline_path.read_text()) == {"line": 90.0, "branch": 60.0}


def test_cobertura_が無ければ失敗_fail_closed(load_script, tmp_path, monkeypatch, set_argv):
    mod = load_script(SCRIPT)
    monkeypatch.setattr(mod, "baseline_path", tmp_path / "baseline.json")
    set_argv(str(tmp_path / "missing.xml"))
    assert mod.main() == 1


def test_引数なしは_usage_エラー(load_script, set_argv):
    mod = load_script(SCRIPT)
    set_argv()
    assert mod.main() == 2
