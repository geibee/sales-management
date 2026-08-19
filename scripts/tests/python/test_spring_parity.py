"""Spring/F# 差分ハーネス自体の fail-closed 契約を検査する。"""

import json
import re

import pytest


PARITY_SCRIPT = "apps/api-spring/scripts/parity-check.py"


def test_parity_JSON_は順序だけ正規化し業務IDと時刻を保持する(load_script):
    mod = load_script(PARITY_SCRIPT)

    normalized, parsed = mod.normalize_body(
        "application/json; charset=utf-8",
        b'{"timestamp":"2098-01-01T00:00:00Z","id":"2098-1-1","value":1.0}',
    )

    assert normalized == (
        '{"id":"2098-1-1","timestamp":"2098-01-01T00:00:00Z","value":1.0}'
    )
    assert parsed["id"] == "2098-1-1"
    assert mod.normalize_header("content-type", "Application/JSON; charset=UTF-8") == (
        "application/json;charset=utf-8"
    )


def test_parity_全35_operation_をシナリオが参照する(load_script):
    mod = load_script(PARITY_SCRIPT)
    source = mod.Path(mod.__file__).read_text(encoding="utf-8")
    observed = set(re.findall(r'self\.call\(\s*"([A-Za-z][A-Za-z0-9]*)"', source))
    ledger = json.loads((mod.ROOT / "parity-ledger.json").read_text(encoding="utf-8"))

    assert observed == set(ledger["operations"])


def test_parity_レスポンス差分を見逃さずtranscriptを残す(load_script, monkeypatch, tmp_path):
    mod = load_script(PARITY_SCRIPT)
    calls = iter(
        [
            mod.Response(200, {"content-type": "application/json"}, '{"id":"F"}', {"id": "F"}),
            mod.Response(200, {"content-type": "application/json"}, '{"id":"J"}', {"id": "J"}),
        ]
    )
    monkeypatch.setattr(mod, "request", lambda *_args, **_kwargs: next(calls))
    output = tmp_path / "parity.json"
    scenario = mod.ParityScenario("http://fsharp", "http://spring", output)

    with pytest.raises(AssertionError, match="HTTP parity 不一致"):
        scenario.call("healthCheck", "GET", "/health")

    transcript = json.loads(output.read_text(encoding="utf-8"))
    assert transcript["requests"][0]["fsharp"]["body"] == '{"id":"F"}'
    assert transcript["requests"][0]["spring"]["body"] == '{"id":"J"}'
