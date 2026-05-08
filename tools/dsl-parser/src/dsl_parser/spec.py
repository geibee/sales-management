"""DSL AST を照合向けの正規化 spec JSON へ変換する。"""

from __future__ import annotations

from typing import Any

from dsl_parser.parser import (
    BehaviorDecl,
    DataDecl,
    DataExpr,
    Identifier,
    ListType,
    OptionalType,
    ProductType,
    Program,
    SumType,
)


def to_spec_dict(program: Program) -> dict[str, Any]:
    """Return a deterministic, implementation-friendly spec dictionary."""
    data_decls: list[dict[str, Any]] = []
    behavior_decls: list[dict[str, Any]] = []

    for declaration in program.declarations:
        if isinstance(declaration, DataDecl):
            data_decls.append(_data_decl_to_spec(declaration))
        elif isinstance(declaration, BehaviorDecl):
            behavior_decls.append(_behavior_decl_to_spec(declaration))

    return {
        "schemaVersion": 1,
        "data": data_decls,
        "behaviors": behavior_decls,
    }


def _data_decl_to_spec(declaration: DataDecl) -> dict[str, Any]:
    value = declaration.value
    if isinstance(value, SumType):
        return {
            "name": declaration.name,
            "kind": "or",
            "variants": [_type_expr_to_spec(v) for v in value.variants],
        }
    if isinstance(value, ProductType):
        return {
            "name": declaration.name,
            "kind": "and",
            "components": [_type_expr_to_spec(c) for c in value.components],
        }
    return {
        "name": declaration.name,
        "kind": "alias",
        "target": _type_expr_to_spec(value),
    }


def _behavior_decl_to_spec(declaration: BehaviorDecl) -> dict[str, Any]:
    return {
        "name": declaration.name,
        "input": _type_expr_to_spec(declaration.input),
        "output": _type_expr_to_spec(declaration.output),
        "error": _type_expr_to_spec(declaration.error),
    }


def _type_expr_to_spec(expr: DataExpr) -> dict[str, Any]:
    if isinstance(expr, Identifier):
        return _named_type(expr.name, optional=False, list_=False)
    if isinstance(expr, OptionalType):
        return _named_type(expr.inner.name, optional=True, list_=False)
    if isinstance(expr, ListType):
        return _named_type(expr.element.name, optional=False, list_=True)
    if isinstance(expr, ProductType):
        return {
            "kind": "and",
            "components": [_type_expr_to_spec(c) for c in expr.components],
        }
    if isinstance(expr, SumType):
        return {
            "kind": "or",
            "variants": [_type_expr_to_spec(v) for v in expr.variants],
        }
    raise TypeError(f"unsupported data expression: {expr!r}")


def _named_type(name: str, *, optional: bool, list_: bool) -> dict[str, Any]:
    return {
        "kind": "named",
        "name": name,
        "optional": optional,
        "list": list_,
    }
