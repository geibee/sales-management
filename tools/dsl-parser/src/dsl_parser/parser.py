"""Minimal DSL parser (P1-1 scope).

Grammar covered (subset of dsl/grammar.ebnf [CORE]):

    program     = { dataDecl } ;
    dataDecl    = "data" identifier "=" identifier ;
    identifier  = ( letter | japaneseChar | "_" ) { letter | digit | japaneseChar | "_" } ;

Whitespace and ``//`` line comments are skipped between tokens.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any

from parsimonious.grammar import Grammar
from parsimonious.nodes import Node, NodeVisitor


_GRAMMAR = Grammar(
    r"""
    program     = _ decls _
    decls       = (dataDecl _)*
    dataDecl    = "data" __ identifier _ "=" _ identifier

    identifier  = id_start id_rest*
    id_start    = ~r"[A-Za-z_　-鿿゠-ヿ぀-ゟ]"
    id_rest     = ~r"[A-Za-z0-9_　-鿿゠-ヿ぀-ゟ]"

    _           = (ws / comment)*
    __          = (ws / comment)+
    ws          = ~r"[ \t\r\n]+"
    comment     = ~r"//[^\n]*"
    """
)


@dataclass(frozen=True)
class Identifier:
    name: str

    def to_dict(self) -> dict[str, Any]:
        return {"type": "Identifier", "name": self.name}


@dataclass(frozen=True)
class DataDecl:
    name: str
    value: Identifier

    def to_dict(self) -> dict[str, Any]:
        return {
            "type": "DataDecl",
            "name": self.name,
            "value": self.value.to_dict(),
        }


@dataclass(frozen=True)
class Program:
    declarations: tuple[DataDecl, ...]

    def to_dict(self) -> dict[str, Any]:
        return {
            "type": "Program",
            "declarations": [d.to_dict() for d in self.declarations],
        }


class _AstBuilder(NodeVisitor):
    def visit_program(self, node: Node, visited_children: list[Any]) -> Program:
        _, decls, _ = visited_children
        return Program(declarations=tuple(decls))

    def visit_decls(self, node: Node, visited_children: list[Any]) -> list[DataDecl]:
        return [child[0] for child in visited_children]

    def visit_dataDecl(self, node: Node, visited_children: list[Any]) -> DataDecl:
        _, _, name, _, _, _, value = visited_children
        return DataDecl(name=name.name, value=value)

    def visit_identifier(self, node: Node, visited_children: list[Any]) -> Identifier:
        return Identifier(name=node.text)

    def generic_visit(self, node: Node, visited_children: list[Any]) -> Any:
        return visited_children or node


def parse(source: str) -> Program:
    """Parse DSL source text and return an AST.

    Raises ``parsimonious.exceptions.ParseError`` on syntax errors.
    """
    tree = _GRAMMAR.parse(source)
    return _AstBuilder().visit(tree)
