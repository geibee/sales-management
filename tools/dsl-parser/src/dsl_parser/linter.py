"""spec.json と F# Domain 実装の最小照合リンター。"""

from __future__ import annotations

import argparse
import json
import re
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any


@dataclass(frozen=True)
class LintFinding:
    code: str
    message: str


@dataclass(frozen=True)
class LintSummary:
    checked_types: int
    checked_behaviors: int
    skipped_due_to_missing_glossary: int


@dataclass(frozen=True)
class LintResult:
    findings: list[LintFinding]
    summary: LintSummary


def lint_spec_against_fsharp(
    spec: dict[str, Any],
    domain_dir: Path,
    glossary_path: Path,
) -> list[LintFinding]:
    """照合可能な辞書項目だけを対象に、DSL spec と F# 実装を比較する。"""
    return lint_spec_against_fsharp_with_summary(spec, domain_dir, glossary_path).findings


def lint_spec_against_fsharp_with_summary(
    spec: dict[str, Any],
    domain_dir: Path,
    glossary_path: Path,
) -> LintResult:
    """照合結果と、辞書未整備で未照合になった範囲の集計を返す。"""
    glossary = load_glossary(glossary_path)
    fsharp_source = _read_fsharp_source(domain_dir)
    summary_counts = _MutableSummary()

    findings: list[LintFinding] = []
    findings.extend(_lint_or_types(spec, glossary, fsharp_source, summary_counts))
    findings.extend(_lint_behaviors(spec, glossary, fsharp_source, summary_counts))
    return LintResult(
        findings=findings,
        summary=LintSummary(
            checked_types=summary_counts.checked_types,
            checked_behaviors=summary_counts.checked_behaviors,
            skipped_due_to_missing_glossary=summary_counts.skipped_due_to_missing_glossary,
        ),
    )


@dataclass
class _MutableSummary:
    checked_types: int = 0
    checked_behaviors: int = 0
    skipped_due_to_missing_glossary: int = 0


def load_glossary(path: Path) -> dict[str, str]:
    """最小の glossary.yaml を読む。

    初手は ``日本語名: EnglishName`` のフラットなマッピングだけを扱う。
    """
    glossary: dict[str, str] = {}
    for line_number, raw_line in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
        line = raw_line.split("#", 1)[0].strip()
        if not line:
            continue
        if ":" not in line:
            raise ValueError(f"invalid glossary line {line_number}: {raw_line}")
        source, target = line.split(":", 1)
        source_name = source.strip().strip("\"'")
        target_name = target.strip().strip("\"'")
        if not source_name or not target_name:
            raise ValueError(f"invalid glossary line {line_number}: {raw_line}")
        glossary[source_name] = target_name
    return glossary


def _lint_or_types(
    spec: dict[str, Any],
    glossary: dict[str, str],
    fsharp_source: str,
    summary: _MutableSummary,
) -> list[LintFinding]:
    findings: list[LintFinding] = []

    for declaration in spec.get("data", []):
        if declaration.get("kind") != "or":
            continue

        dsl_name = declaration.get("name")
        if not isinstance(dsl_name, str):
            continue
        if dsl_name not in glossary:
            summary.skipped_due_to_missing_glossary += 1
            continue

        variant_names = _named_children(declaration.get("variants", []))
        summary.checked_types += 1
        missing_variant_names = [name for name in variant_names if name not in glossary]
        for variant_name in missing_variant_names:
            findings.append(
                LintFinding(
                    code="missing-glossary-entry",
                    message=(
                        f"DSL data '{dsl_name}' の variant '{variant_name}' が "
                        "glossary.yaml に登録されていません。"
                    ),
                )
            )

        fsharp_type_name = glossary[dsl_name]
        union_block = _find_union_block(fsharp_source, fsharp_type_name)
        if union_block is None:
            findings.append(
                LintFinding(
                    code="missing-union-type",
                    message=f"DSL data '{dsl_name}' に対応する F# DU 型 '{fsharp_type_name}' が見つかりません。",
                )
            )
            continue

        for variant_name in variant_names:
            if variant_name not in glossary:
                continue
            fsharp_variant_type = glossary[variant_name]
            if not _union_block_has_variant(union_block, fsharp_variant_type):
                findings.append(
                    LintFinding(
                        code="missing-union-variant",
                        message=(
                            f"DSL data '{dsl_name}' の variant '{variant_name}' に対応する "
                            f"F# variant/payload '{fsharp_variant_type}' が見つかりません。"
                        ),
                    )
                )

    return findings


def _lint_behaviors(
    spec: dict[str, Any],
    glossary: dict[str, str],
    fsharp_source: str,
    summary: _MutableSummary,
) -> list[LintFinding]:
    findings: list[LintFinding] = []

    for behavior in spec.get("behaviors", []):
        dsl_name = behavior.get("name")
        if not isinstance(dsl_name, str):
            continue
        if dsl_name not in glossary:
            if _behavior_references_glossary_scope(behavior, glossary):
                findings.append(
                    LintFinding(
                        code="missing-glossary-entry",
                        message=f"DSL behavior '{dsl_name}' が glossary.yaml に登録されていません。",
                    )
                )
            else:
                summary.skipped_due_to_missing_glossary += 1
            continue

        function_name = glossary[dsl_name]
        summary.checked_behaviors += 1
        if not _has_function(fsharp_source, function_name):
            findings.append(
                LintFinding(
                    code="missing-behavior-function",
                    message=f"DSL behavior '{dsl_name}' に対応する F# 関数 '{function_name}' が見つかりません。",
                )
            )

    return findings


def _behavior_references_glossary_scope(behavior: dict[str, Any], glossary: dict[str, str]) -> bool:
    referenced_names = set()
    referenced_names.update(_collect_named_types(behavior.get("input")))
    referenced_names.update(_collect_named_types(behavior.get("output")))
    return bool(referenced_names) and all(name in glossary for name in referenced_names)


def _collect_named_types(value: Any) -> list[str]:
    if not isinstance(value, dict):
        return []
    if value.get("kind") == "named" and isinstance(value.get("name"), str):
        return [value["name"]]
    names: list[str] = []
    for key in ("components", "variants"):
        children = value.get(key)
        if isinstance(children, list):
            for child in children:
                names.extend(_collect_named_types(child))
    return names


def _read_fsharp_source(domain_dir: Path) -> str:
    parts: list[str] = []
    for path in sorted(domain_dir.glob("*.fs")):
        parts.append(path.read_text(encoding="utf-8"))
    return "\n".join(parts)


def _named_children(value: Any) -> list[str]:
    names: list[str] = []
    if not isinstance(value, list):
        return names
    for child in value:
        if isinstance(child, dict) and child.get("kind") == "named" and isinstance(child.get("name"), str):
            names.append(child["name"])
    return names


def _find_union_block(source: str, type_name: str) -> str | None:
    escaped = re.escape(type_name)
    pattern = re.compile(
        rf"^type\s+{escaped}\b[^\n]*=(?P<body>(?:\n(?:[ \t].*|$))*)",
        re.MULTILINE,
    )
    match = pattern.search(source)
    if match is None:
        return None
    return match.group("body")


def _union_block_has_variant(union_block: str, fsharp_variant_type: str) -> bool:
    escaped = re.escape(fsharp_variant_type)
    direct_case = re.compile(rf"^\s*\|\s+{escaped}\b", re.MULTILINE)
    payload_case = re.compile(rf"^\s*\|\s+[A-Za-z_][A-Za-z0-9_']*\s+of\s+{escaped}\b", re.MULTILINE)
    return bool(direct_case.search(union_block) or payload_case.search(union_block))


def _has_function(source: str, function_name: str) -> bool:
    escaped = re.escape(function_name)
    pattern = re.compile(rf"^\s*let\s+(?:private\s+)?(?:inline\s+)?{escaped}\b", re.MULTILINE)
    return bool(pattern.search(source))


def main() -> None:
    parser = argparse.ArgumentParser(description="spec.json と F# Domain 実装を照合する")
    parser.add_argument("--spec", required=True, type=Path, help="dsl-parser --format spec の出力JSON")
    parser.add_argument("--domain-dir", required=True, type=Path, help="SalesManagement/Domain の *.fs ディレクトリ")
    parser.add_argument("--glossary", required=True, type=Path, help="DSL名からF#名への glossary.yaml")
    args = parser.parse_args()

    spec = json.loads(args.spec.read_text(encoding="utf-8"))
    result = lint_spec_against_fsharp_with_summary(spec, args.domain_dir, args.glossary)
    findings = result.findings
    summary = result.summary
    if findings:
        for finding in findings:
            print(f"{finding.code}: {finding.message}", file=sys.stderr)
        print(_format_summary(summary), file=sys.stderr)
        sys.exit(1)

    print(f"OK {_format_summary(summary)}")


def _format_summary(summary: LintSummary) -> str:
    return (
        f"checkedTypes={summary.checked_types} "
        f"checkedBehaviors={summary.checked_behaviors} "
        f"skippedDueToMissingGlossary={summary.skipped_due_to_missing_glossary}"
    )
