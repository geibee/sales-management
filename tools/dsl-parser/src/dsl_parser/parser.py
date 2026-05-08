"""DSL parser for the [CORE] subset of dsl/grammar.ebnf.

Currently supports:

    program       = { declaration } ;
    declaration   = dataDecl | behaviorDecl ;
    dataDecl      = "data" identifier "=" dataExpr ;
    behaviorDecl  = "behavior" identifier "=" productExpr "->" productExpr "OR" identifier ;
    dataExpr      = sumExpr ;
    sumExpr       = productExpr { "OR" productExpr } ;
    productExpr   = atomType  { "AND" atomType  } ;
    atomType      = optionalType | listType | "(" dataExpr ")" | identifier ;
    optionalType  = identifier "?" ;
    listType      = "List" "<" identifier ">" ;

The [VERIFICATION] layer (where/requires/ensures/invariant/property/initial)
is out of scope and will be added in P4.

Whitespace and ``//`` line comments are skipped between tokens.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Union

from parsimonious.grammar import Grammar
from parsimonious.nodes import Node, NodeVisitor


_GRAMMAR = Grammar(
    r"""
    program       = _ decls _
    decls         = (declaration _)*
    declaration   = dataDecl / behaviorDecl
    dataDecl      = "data" __ identifier _ "=" _ dataExpr
    behaviorDecl  = "behavior" __ identifier _ "=" _ productExpr _ "->" _ productExpr _ "OR" __ identifier

    dataExpr      = sumExpr
    sumExpr       = productExpr orRest
    orRest        = (_ "OR" __ productExpr)*
    productExpr   = atomType andRest
    andRest       = (_ "AND" __ atomType)*

    atomType      = optionalType / listType / parenType / identifier
    parenType     = "(" _ dataExpr _ ")"
    optionalType  = identifier _ "?"
    listType      = "List" _ "<" _ identifier _ ">" listConstraint?
    listConstraint = ~r"[ \t]*//[^\n]*1件以上[^\n]*"

    identifier    = id_start id_rest*
    id_start      = ~r"[A-Za-z_　-鿿゠-ヿ぀-ゟ]"
    id_rest       = ~r"[A-Za-z0-9_　-鿿゠-ヿ぀-ゟ]"

    _             = (ws / comment)*
    __            = (ws / comment)+
    ws            = ~r"[ \t\r\n]+"
    comment       = ~r"//[^\n]*"
    """
)


# ============================================================
# AST nodes
# ============================================================


@dataclass(frozen=True)
class Identifier:
    name: str

    def to_dict(self) -> dict[str, Any]:
        return {"type": "Identifier", "name": self.name}


@dataclass(frozen=True)
class OptionalType:
    inner: Identifier

    def to_dict(self) -> dict[str, Any]:
        return {"type": "OptionalType", "inner": self.inner.to_dict()}


@dataclass(frozen=True)
class ListType:
    element: Identifier
    non_empty: bool = False

    def to_dict(self) -> dict[str, Any]:
        return {"type": "ListType", "element": self.element.to_dict()}


@dataclass(frozen=True)
class ProductType:
    components: tuple["DataExpr", ...]

    def to_dict(self) -> dict[str, Any]:
        return {
            "type": "ProductType",
            "components": [c.to_dict() for c in self.components],
        }


@dataclass(frozen=True)
class SumType:
    variants: tuple["DataExpr", ...]

    def to_dict(self) -> dict[str, Any]:
        return {
            "type": "SumType",
            "variants": [v.to_dict() for v in self.variants],
        }


DataExpr = Union[Identifier, OptionalType, ListType, ProductType, SumType]


@dataclass(frozen=True)
class DataDecl:
    name: str
    value: DataExpr

    def to_dict(self) -> dict[str, Any]:
        return {
            "type": "DataDecl",
            "name": self.name,
            "value": self.value.to_dict(),
        }


@dataclass(frozen=True)
class BehaviorDecl:
    name: str
    input: DataExpr
    output: DataExpr
    error: Identifier

    def to_dict(self) -> dict[str, Any]:
        return {
            "type": "BehaviorDecl",
            "name": self.name,
            "input": self.input.to_dict(),
            "output": self.output.to_dict(),
            "error": self.error.to_dict(),
        }


Declaration = Union[DataDecl, BehaviorDecl]


@dataclass(frozen=True)
class Program:
    declarations: tuple[Declaration, ...]

    def to_dict(self) -> dict[str, Any]:
        return {
            "type": "Program",
            "declarations": [d.to_dict() for d in self.declarations],
        }


# ============================================================
# AST builder
# ============================================================


class _AstBuilder(NodeVisitor):
    def visit_program(self, node: Node, visited_children: list[Any]) -> Program:
        _, decls, _ = visited_children
        return Program(declarations=tuple(decls))

    def visit_decls(self, node: Node, visited_children: list[Any]) -> list[Declaration]:
        return [child[0] for child in visited_children]

    def visit_declaration(self, node: Node, visited_children: list[Any]) -> Declaration:
        return visited_children[0]

    def visit_dataDecl(self, node: Node, visited_children: list[Any]) -> DataDecl:
        _, _, name, _, _, _, value = visited_children
        return DataDecl(name=name.name, value=value)

    def visit_behaviorDecl(self, node: Node, visited_children: list[Any]) -> BehaviorDecl:
        # "behavior" __ identifier _ "=" _ productExpr _ "->" _ productExpr _ "OR" __ identifier
        _, _, name, _, _, _, input_, _, _, _, output, _, _, _, error = visited_children
        return BehaviorDecl(name=name.name, input=input_, output=output, error=error)

    def visit_dataExpr(self, node: Node, visited_children: list[Any]) -> DataExpr:
        return visited_children[0]

    def visit_sumExpr(self, node: Node, visited_children: list[Any]) -> DataExpr:
        head, rest = visited_children
        if not rest:
            return head
        return SumType(variants=(head, *rest))

    def visit_orRest(self, node: Node, visited_children: list[Any]) -> list[DataExpr]:
        return [child[3] for child in visited_children]

    def visit_productExpr(self, node: Node, visited_children: list[Any]) -> DataExpr:
        head, rest = visited_children
        if not rest:
            return head
        return ProductType(components=(head, *rest))

    def visit_andRest(self, node: Node, visited_children: list[Any]) -> list[DataExpr]:
        return [child[3] for child in visited_children]

    def visit_atomType(self, node: Node, visited_children: list[Any]) -> DataExpr:
        return visited_children[0]

    def visit_parenType(self, node: Node, visited_children: list[Any]) -> DataExpr:
        return visited_children[2]

    def visit_optionalType(self, node: Node, visited_children: list[Any]) -> OptionalType:
        ident, _, _ = visited_children
        return OptionalType(inner=ident)

    def visit_listType(self, node: Node, visited_children: list[Any]) -> ListType:
        # "List" _ "<" _ identifier _ ">" listConstraint?
        return ListType(element=visited_children[4], non_empty=bool(visited_children[7]))

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
