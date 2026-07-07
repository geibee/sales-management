"""CI スクリプト (python) 単体テストの共通フィクスチャ (issue #9 Tier2-17)。

対象スクリプトはハイフン入りファイル名 (coverage-ratchet.py 等) のため
通常の import ができない。importlib でファイルパスから直接ロードする。
"""

import importlib.util
import sys
from pathlib import Path

import pytest

REPO_ROOT = Path(__file__).resolve().parents[3]


@pytest.fixture
def load_script():
    """リポジトリ相対パスのスクリプトをモジュールとしてロードする。"""

    def _load(relpath: str):
        path = REPO_ROOT / relpath
        name = path.stem.replace("-", "_")
        spec = importlib.util.spec_from_file_location(name, path)
        mod = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(mod)
        return mod

    return _load


@pytest.fixture
def set_argv(monkeypatch):
    """sys.argv をテスト入力に差し替える。"""

    def _set(*args: str):
        monkeypatch.setattr(sys, "argv", ["prog", *args])

    return _set
