"""sarif-to-lessons.py (Stop フックの LESSONS.md 自動更新) の単体テスト。

「人間の編集を壊さない」「ignore 判断を恒久化する」というメモリ品質の
契約をリグレッションから守る (issue #9 Tier2-17)。
"""

import json
from datetime import date

import pytest

SCRIPT = ".claude/scripts/sarif-to-lessons.py"


def make_sarif(rule_counts: dict[str, int], tool: str = "Schemathesis") -> str:
    results = []
    for rule, n in rule_counts.items():
        results.extend({"ruleId": rule, "message": {"text": f"{rule} detected"}} for _ in range(n))
    return json.dumps(
        {"version": "2.1.0", "runs": [{"tool": {"driver": {"name": tool}}, "results": results}]}
    )


@pytest.fixture
def lessons(load_script, tmp_path, monkeypatch):
    mod = load_script(SCRIPT)
    sarif_path = tmp_path / "merged.sarif"
    lessons_path = tmp_path / "LESSONS.md"
    monkeypatch.setattr(mod, "SARIF_PATH", sarif_path)
    monkeypatch.setattr(mod, "LESSONS_PATH", lessons_path)

    def _run(rule_counts: dict[str, int]) -> int:
        sarif_path.write_text(make_sarif(rule_counts))
        return mod.main()

    _run.mod = mod
    _run.path = lessons_path
    return _run


def test_sarif_が無ければ何もしない(load_script, tmp_path, monkeypatch):
    mod = load_script(SCRIPT)
    monkeypatch.setattr(mod, "SARIF_PATH", tmp_path / "missing.sarif")
    monkeypatch.setattr(mod, "LESSONS_PATH", tmp_path / "LESSONS.md")
    assert mod.main() == 0
    assert not (tmp_path / "LESSONS.md").exists()


def test_閾値以上の検出は_LESSONS_のマーカ間に記録される(lessons):
    assert lessons({"server_error": 3, "rare_noise": 1}) == 0
    text = lessons.path.read_text()
    begin = text.index(lessons.mod.BEGIN_MARK)
    end = text.index(lessons.mod.END_MARK)
    section = text[begin:end]
    assert "`Schemathesis.server_error`" in section
    assert f"最終検出 {date.today().isoformat()} / 直近 3件" in section
    # 閾値 (3) 未満は記録されない
    assert "rare_noise" not in text
    # ツール別の対応ヒントが付く
    assert "schemathesis-hooks.py" in section


def test_既存エントリは本文の手動編集を保持したまま日付と件数だけ更新する(lessons):
    assert lessons({"server_error": 3}) == 0
    # 人間が本文を蒸留編集した状態を再現する
    text = lessons.path.read_text()
    edited = text.replace("server_error detected", "蒸留済みメモ: NRE は null guard で対応")
    old = edited.replace(date.today().isoformat(), "2026-01-01")
    lessons.path.write_text(old)

    assert lessons({"server_error": 5}) == 0
    updated = lessons.path.read_text()
    assert "蒸留済みメモ: NRE は null guard で対応" in updated
    assert f"最終検出 {date.today().isoformat()} / 直近 5件" in updated


def test_lessons_ignore_で該当エントリは削除され再記録もされない(lessons):
    assert lessons({"server_error": 3}) == 0
    text = lessons.path.read_text()
    lessons.path.write_text(
        text + "\n<!-- lessons:ignore `Schemathesis.server_error` ノイズと判断 -->\n"
    )

    assert lessons({"server_error": 10}) == 0
    updated = lessons.path.read_text()
    section = updated[updated.index(lessons.mod.BEGIN_MARK) : updated.index(lessons.mod.END_MARK)]
    assert "`Schemathesis.server_error`" not in section  # エントリは削除済み
    assert "lessons:ignore" in updated  # 人間の判断は残る


def test_マーカ外のテキストは変更しない(lessons):
    header = "# 人間が書いたヘッダ\n\n手書きメモは維持されること。\n\n"
    lessons.path.write_text(
        header + lessons.mod.BEGIN_MARK + "\n" + lessons.mod.END_MARK + "\n"
    )

    assert lessons({"server_error": 4}) == 0
    updated = lessons.path.read_text()
    assert updated.startswith(header)
    assert "`Schemathesis.server_error`" in updated


def test_上限を超えたら最終検出が古いものから削除される(lessons, monkeypatch):
    monkeypatch.setattr(lessons.mod, "MAX_ENTRIES", 2)
    assert lessons({"a": 3}) == 0
    # a を過去日付に付け替えてから b, c を検出させる
    text = lessons.path.read_text().replace(date.today().isoformat(), "2026-01-01")
    lessons.path.write_text(text)

    assert lessons({"b": 3, "c": 3}) == 0
    updated = lessons.path.read_text()
    assert "`Schemathesis.a`" not in updated
    assert "`Schemathesis.b`" in updated
    assert "`Schemathesis.c`" in updated
