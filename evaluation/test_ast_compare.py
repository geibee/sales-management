"""ast_compare.py のユニットテスト。

リポジトリルートから:
    python3 evaluation/test_ast_compare.py
"""

from __future__ import annotations

import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from ast_compare import (  # noqa: E402
    ast_equal,
    extract_alloy_decls,
    extract_fsharp_decls,
    extract_tla_decls,
    parse_mermaid,
)


class TestMermaidGraph(unittest.TestCase):
    def test_parse_simple(self):
        src = """\
stateDiagram-v2
    [*] --> A
    A --> B : do
    B --> [*]
"""
        g = parse_mermaid(src)
        self.assertEqual(
            g,
            frozenset({
                ("[*]", "A", ""),
                ("A", "B", "do"),
                ("B", "[*]", ""),
            }),
        )

    def test_reordering_is_equivalent(self):
        a = """\
stateDiagram-v2
    [*] --> A
    A --> B : do
"""
        b = """\
stateDiagram-v2
    A --> B : do
    [*] --> A
"""
        self.assertTrue(ast_equal(a, b, "mmd"))

    def test_missing_transition_detected(self):
        a = """\
stateDiagram-v2
    A --> B : do
    B --> C : go
"""
        b = """\
stateDiagram-v2
    A --> B : do
"""
        self.assertFalse(ast_equal(a, b, "mmd"))

    def test_label_difference_detected(self):
        a = "stateDiagram-v2\n    A --> B : do"
        b = "stateDiagram-v2\n    A --> B : went"
        self.assertFalse(ast_equal(a, b, "mmd"))

    def test_comments_ignored(self):
        a = "%% header\nstateDiagram-v2\n    %% inline\n    A --> B"
        b = "stateDiagram-v2\n    A --> B"
        self.assertTrue(ast_equal(a, b, "mmd"))


class TestFSharpDecls(unittest.TestCase):
    def test_extract_top_level(self):
        src = """\
namespace X
open System

type A = A of int
type B = { f: int }

let work x = x + 1
"""
        d = extract_fsharp_decls(src)
        self.assertIn("X", d)
        self.assertIn("System", d)
        self.assertIn("A", d)
        self.assertIn("B", d)
        self.assertIn("work", d)

    def test_reordering_is_equivalent(self):
        a = "type A = A of int\ntype B = B of int"
        b = "type B = B of int\ntype A = A of int"
        self.assertTrue(ast_equal(a, b, "fs"))

    def test_body_difference_detected(self):
        a = "type A = A of int"
        b = "type A = A of string"
        self.assertFalse(ast_equal(a, b, "fs"))


class TestAlloyDecls(unittest.TestCase):
    def test_extract_top_level(self):
        src = """\
module M

sig A {}
sig B { f: A }
fact NonEmpty { all a: A | a in B.f }
pred check[a: A] {}
"""
        d = extract_alloy_decls(src)
        self.assertIn("M", d)
        self.assertIn("A", d)
        self.assertIn("B", d)
        self.assertIn("NonEmpty", d)
        self.assertIn("check", d)

    def test_reordering_is_equivalent(self):
        a = "sig A {}\nsig B {}"
        b = "sig B {}\nsig A {}"
        self.assertTrue(ast_equal(a, b, "als"))


class TestTLADecls(unittest.TestCase):
    def test_extract_top_level(self):
        src = """\
---- MODULE M ----
EXTENDS Naturals
CONSTANTS X, Y
VARIABLES s

Foo == s = 0
Bar(x) == x + 1
====
"""
        d = extract_tla_decls(src)
        self.assertIn("M", d)
        self.assertIn("EXTENDS", d)
        self.assertIn("CONSTANTS", d)
        self.assertIn("VARIABLES", d)
        self.assertIn("Foo", d)
        self.assertIn("Bar", d)

    def test_reordering_is_equivalent(self):
        a = "Foo == 1\nBar == 2"
        b = "Bar == 2\nFoo == 1"
        self.assertTrue(ast_equal(a, b, "tla"))


class TestUnsupported(unittest.TestCase):
    def test_raises_value_error(self):
        with self.assertRaises(ValueError):
            ast_equal("a", "b", "py")


if __name__ == "__main__":
    unittest.main()
