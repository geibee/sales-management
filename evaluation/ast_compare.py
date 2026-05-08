#!/usr/bin/env python3
"""ターゲット別 AST/構造比較。

normalize.py が「コメント・空行差」を吸収するのに対し、こちらは
「宣言順序差」までを吸収する。

サポート:
    .mmd  Mermaid stateDiagram-v2 をグラフ AST にパースし、
          (from, to, label) の集合として比較
    .fs   F# / .als Alloy / .tla TLA+ はトップレベル宣言を
          ブロック単位で抽出し、{ name -> normalized_body } の
          dict として比較（順序差を吸収）

使い方:
    python3 ast_compare.py <ext> <path1> <path2>

終了コード:
    0  AST/構造的に等価
    1  差分あり
    2  使用法エラー
"""

from __future__ import annotations

import re
import sys
from typing import Dict, FrozenSet, Tuple

from normalize import normalize


# ============================================================
# Mermaid: 完全なグラフ AST
# ============================================================


def parse_mermaid(text: str) -> FrozenSet[Tuple[str, str, str]]:
    """stateDiagram-v2 を (from, to, label) の集合にパース。"""
    # コメントを除去
    text = re.sub(r"%%.*$", "", text, flags=re.MULTILINE)

    transitions: set[Tuple[str, str, str]] = set()
    in_diagram = False

    for raw in text.split("\n"):
        line = raw.strip()
        if not line:
            continue
        if "stateDiagram" in line:
            in_diagram = True
            continue
        if not in_diagram:
            continue
        if "-->" not in line:
            continue

        left, right = line.split("-->", 1)
        from_state = left.strip()
        if ":" in right:
            to_state, label = right.split(":", 1)
            to_state = to_state.strip()
            label = label.strip()
        else:
            to_state = right.strip()
            label = ""

        transitions.add((from_state, to_state, label))

    return frozenset(transitions)


# ============================================================
# F# / Alloy / TLA+: トップレベル宣言ダイジェスト
# ============================================================


def _split_top_level_blocks(text: str, head_pattern: re.Pattern[str]) -> Dict[str, str]:
    """先頭が head_pattern にマッチする行をブロック開始とみなし、
    次のブロック開始までを 1 ブロックとして辞書を作る。

    各ブロックの「名前」は head_pattern のキャプチャグループ 1 から取る。
    本体は名前を含む全行を改行で結合し、空白を正規化したもの。
    重複名がある場合は配列風に key#N で区別。
    """
    lines = text.split("\n")
    blocks: list[Tuple[str, list[str]]] = []
    current_name: str | None = None
    current_body: list[str] = []

    for line in lines:
        match = head_pattern.match(line)
        if match:
            if current_name is not None:
                blocks.append((current_name, current_body))
            current_name = match.group(1).strip()
            current_body = [line]
        elif current_name is not None:
            current_body.append(line)
    if current_name is not None:
        blocks.append((current_name, current_body))

    out: Dict[str, str] = {}
    seen: dict[str, int] = {}
    for name, body in blocks:
        body_text = "\n".join(b.rstrip() for b in body if b.strip())
        # 内部の連続空白を畳む
        body_text = re.sub(r"[ \t]+", " ", body_text)
        if name in seen:
            seen[name] += 1
            key = f"{name}#{seen[name]}"
        else:
            seen[name] = 0
            key = name
        out[key] = body_text
    return out


# F#: type / let / module / namespace / open / and で始まる行（インデント 0）
_FSHARP_HEAD = re.compile(
    r"^(?:type|let|module|namespace|open|and)\s+([A-Za-z_][\w']*|\([^)]+\))"
)


def extract_fsharp_decls(text: str) -> Dict[str, str]:
    text = normalize(text, "fs")
    return _split_top_level_blocks(text, _FSHARP_HEAD)


# Alloy: module / sig / abstract sig / one sig / pred / fun / fact / assert /
# run / check で始まる行（インデント 0）
_ALLOY_HEAD = re.compile(
    r"^(?:module|abstract\s+sig|one\s+sig|sig|pred|fun|fact|assert|run|check)"
    r"\s+([A-Za-z_][\w]*)",
)


def extract_alloy_decls(text: str) -> Dict[str, str]:
    text = normalize(text, "als")
    return _split_top_level_blocks(text, _ALLOY_HEAD)


# TLA+: ---- MODULE / EXTENDS / CONSTANTS / VARIABLES / 識別子 == の各行
_TLA_HEAD = re.compile(
    r"^(?:----\s+MODULE\s+([A-Za-z_]\w*)"
    r"|(EXTENDS|CONSTANTS|VARIABLES|VARIABLE)\b"
    r"|([A-Za-z_]\w*)\s*(?:\([^)]*\))?\s*==)"
)


def extract_tla_decls(text: str) -> Dict[str, str]:
    text = normalize(text, "tla")
    lines = text.split("\n")
    blocks: list[Tuple[str, list[str]]] = []
    current_name: str | None = None
    current_body: list[str] = []

    for line in lines:
        m = _TLA_HEAD.match(line)
        if m:
            if current_name is not None:
                blocks.append((current_name, current_body))
            name = m.group(1) or m.group(2) or m.group(3)
            current_name = name.strip()
            current_body = [line]
        elif current_name is not None:
            current_body.append(line)
    if current_name is not None:
        blocks.append((current_name, current_body))

    out: Dict[str, str] = {}
    seen: dict[str, int] = {}
    for name, body in blocks:
        body_text = "\n".join(b.rstrip() for b in body if b.strip())
        body_text = re.sub(r"[ \t]+", " ", body_text)
        if name in seen:
            seen[name] += 1
            key = f"{name}#{seen[name]}"
        else:
            seen[name] = 0
            key = name
        out[key] = body_text
    return out


# ============================================================
# トップレベル比較
# ============================================================


def ast_equal(text1: str, text2: str, ext: str) -> bool:
    if ext == "mmd":
        return parse_mermaid(text1) == parse_mermaid(text2)
    if ext == "fs":
        return extract_fsharp_decls(text1) == extract_fsharp_decls(text2)
    if ext == "als":
        return extract_alloy_decls(text1) == extract_alloy_decls(text2)
    if ext == "tla":
        return extract_tla_decls(text1) == extract_tla_decls(text2)
    raise ValueError(f"unsupported extension: {ext}")


def main() -> int:
    if len(sys.argv) != 4:
        print("usage: ast_compare.py <ext> <path1> <path2>", file=sys.stderr)
        return 2
    ext, p1, p2 = sys.argv[1], sys.argv[2], sys.argv[3]
    try:
        with open(p1, encoding="utf-8") as f:
            t1 = f.read()
        with open(p2, encoding="utf-8") as f:
            t2 = f.read()
    except OSError as e:
        print(f"error: {e}", file=sys.stderr)
        return 2
    try:
        return 0 if ast_equal(t1, t2, ext) else 1
    except ValueError as e:
        print(f"error: {e}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
