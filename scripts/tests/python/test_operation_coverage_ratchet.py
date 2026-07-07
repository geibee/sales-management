"""operation-coverage-ratchet.py (契約カバレッジラチェット) の単体テスト。

「API を追加したのにテスト未到達」の検出と、fail-closed (列挙破壊 / 記録欠如 = 失敗)
を固定する (issue #9 Tier2-17)。
"""

import json

import pytest

SCRIPT = "apps/api-fsharp/scripts/operation-coverage-ratchet.py"

OP_IDS = [f"op{i:02d}" for i in range(12)]


def make_spec(op_ids) -> str:
    return "\n".join(f"      operationId: {oid}" for oid in op_ids) + "\n"


@pytest.fixture
def ratchet(load_script, tmp_path, monkeypatch):
    mod = load_script(SCRIPT)
    monkeypatch.setattr(mod, "spec_path", tmp_path / "openapi.yaml")
    monkeypatch.setattr(mod, "baseline_path", tmp_path / "baseline.json")
    monkeypatch.delenv("RATCHET_UPDATE", raising=False)

    def _run(recorded: list[str], baseline: list[str], spec_ids=OP_IDS) -> int:
        mod.spec_path.write_text(make_spec(spec_ids))
        mod.baseline_path.write_text(json.dumps(baseline))
        rec = tmp_path / "operation-coverage.json"
        rec.write_text(json.dumps(recorded))
        import sys

        monkeypatch.setattr(sys, "argv", ["prog", str(rec)])
        return mod.main()

    _run.mod = mod
    _run.tmp = tmp_path
    return _run


def test_全_operation_到達なら合格(ratchet):
    assert ratchet(recorded=OP_IDS, baseline=[]) == 0


def test_baseline_外の未到達が増えると失敗(ratchet):
    assert ratchet(recorded=OP_IDS[:-1], baseline=[]) == 1


def test_baseline_内の未到達は許容(ratchet):
    assert ratchet(recorded=OP_IDS[:-1], baseline=[OP_IDS[-1]]) == 0


def test_改善時は既定で_baseline_を書き換えない(ratchet):
    assert ratchet(recorded=OP_IDS, baseline=[OP_IDS[-1]]) == 0
    assert json.loads(ratchet.mod.baseline_path.read_text()) == [OP_IDS[-1]]


def test_RATCHET_UPDATE_で_baseline_を縮小する(ratchet, monkeypatch):
    monkeypatch.setenv("RATCHET_UPDATE", "1")
    assert ratchet(recorded=OP_IDS, baseline=[OP_IDS[-1]]) == 0
    assert json.loads(ratchet.mod.baseline_path.read_text()) == []


def test_operationId_の列挙が壊れたら失敗_fail_closed(ratchet):
    # 10 件未満しか列挙できない = spec の書式規約が壊れている
    with pytest.raises(SystemExit) as exc:
        ratchet(recorded=[], baseline=[], spec_ids=["a", "b"])
    assert exc.value.code == 1


def test_記録ファイルが無ければ失敗_fail_closed(load_script, tmp_path, monkeypatch, set_argv):
    mod = load_script(SCRIPT)
    monkeypatch.setattr(mod, "spec_path", tmp_path / "openapi.yaml")
    monkeypatch.setattr(mod, "baseline_path", tmp_path / "baseline.json")
    set_argv(str(tmp_path / "missing.json"))
    assert mod.main() == 1


def test_引数なしは_usage_エラー(load_script, set_argv):
    mod = load_script(SCRIPT)
    set_argv()
    assert mod.main() == 2
