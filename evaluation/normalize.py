#!/usr/bin/env python3
"""ターゲット別ノーマライザー: コメント・空行・末尾空白を除去して
意味論的に比較可能な形に正規化する。

使い方:
    python3 normalize.py <ext> <path>

サポートする拡張子:
    fs   F#
    mmd  Mermaid
    als  Alloy
    tla  TLA+

出力は標準出力。エラーは終了コード 2 と stderr に出す。
"""

from __future__ import annotations

import re
import sys


def _strip_fsharp(text: str) -> str:
    # /// ドキュメントコメントと // 行コメントを除去
    text = re.sub(r"//[^\n]*", "", text)
    return text


def _strip_mermaid(text: str) -> str:
    # %% 行コメントを除去
    text = re.sub(r"%%[^\n]*", "", text)
    return text


def _strip_alloy(text: str) -> str:
    # -- 行コメント
    text = re.sub(r"--[^\n]*", "", text)
    # /* ... */ ブロックコメント
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.DOTALL)
    return text


def _strip_tla(text: str) -> str:
    # (* ... *) ブロックコメント (DOTALL で複数行対応)
    text = re.sub(r"\(\*.*?\*\)", "", text, flags=re.DOTALL)
    # \* 行コメント
    text = re.sub(r"\\\*[^\n]*", "", text)
    return text


_STRIPPERS = {
    "fs": _strip_fsharp,
    "mmd": _strip_mermaid,
    "als": _strip_alloy,
    "tla": _strip_tla,
}


def _normalize_whitespace(text: str) -> str:
    # 各行末空白を除去 → 空行を全削除 → 改行で再連結
    lines = (line.rstrip() for line in text.split("\n"))
    return "\n".join(line for line in lines if line)


def normalize(text: str, ext: str) -> str:
    stripper = _STRIPPERS.get(ext)
    if stripper is None:
        raise ValueError(f"unsupported extension: {ext}")
    return _normalize_whitespace(stripper(text))


def main() -> None:
    if len(sys.argv) != 3:
        print("usage: normalize.py <ext> <path>", file=sys.stderr)
        sys.exit(2)
    ext, path = sys.argv[1], sys.argv[2]
    try:
        with open(path, encoding="utf-8") as f:
            source = f.read()
    except OSError as e:
        print(f"error: {e}", file=sys.stderr)
        sys.exit(2)

    try:
        sys.stdout.write(normalize(source, ext))
        if not sys.stdout.line_buffering or sys.stdout.tell() == 0:
            pass
        sys.stdout.write("\n")
    except ValueError as e:
        print(f"error: {e}", file=sys.stderr)
        sys.exit(2)


if __name__ == "__main__":
    main()
