"""normalize.py のユニットテスト。

リポジトリルートから:
    python3 -m pytest evaluation/test_normalize.py -v

または直接:
    python3 evaluation/test_normalize.py
"""

from __future__ import annotations

import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from normalize import normalize  # noqa: E402


class TestFSharp(unittest.TestCase):
    def test_strips_doc_and_line_comments(self):
        src = """\
/// doc comment
type X = X of int  // inline
let v = 1
"""
        self.assertEqual(normalize(src, "fs"), "type X = X of int\nlet v = 1")

    def test_collapses_blank_lines(self):
        src = "let a = 1\n\n\nlet b = 2\n"
        self.assertEqual(normalize(src, "fs"), "let a = 1\nlet b = 2")


class TestMermaid(unittest.TestCase):
    def test_strips_double_percent_comments(self):
        src = """\
%% header
stateDiagram-v2
    %% section
    [*] --> A
"""
        self.assertEqual(normalize(src, "mmd"), "stateDiagram-v2\n    [*] --> A")


class TestAlloy(unittest.TestCase):
    def test_strips_dash_dash_and_block_comments(self):
        src = """\
-- header
sig X { f: Int }  -- inline
/* block
   comment */
sig Y {}
"""
        self.assertEqual(normalize(src, "als"), "sig X { f: Int }\nsig Y {}")


class TestTLAPlus(unittest.TestCase):
    def test_strips_paren_star_and_backslash_star(self):
        src = """\
(* multiline
   comment *)
EXTENDS Naturals
\\* line comment
Init == x = 0
"""
        self.assertEqual(normalize(src, "tla"), "EXTENDS Naturals\nInit == x = 0")


class TestUnsupported(unittest.TestCase):
    def test_raises_value_error(self):
        with self.assertRaises(ValueError):
            normalize("foo", "py")


if __name__ == "__main__":
    unittest.main()
