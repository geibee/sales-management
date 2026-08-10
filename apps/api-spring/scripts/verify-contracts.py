#!/usr/bin/env python3
"""Spring 実装が共有契約を取りこぼしていないことを fail-closed で検査する。"""

from __future__ import annotations

import hashlib
import json
import re
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
REPOSITORY = ROOT.parents[1]
OPENAPI = REPOSITORY / "apps/api-fsharp/openapi.yaml"
LEDGER = ROOT / "parity-ledger.json"
MIGRATIONS = REPOSITORY / "apps/api-fsharp/migrations"
MIGRATION_ORDER = ROOT / "infrastructure/src/main/resources/db/migration-order.txt"
FSHARP_TEST_ROOT = REPOSITORY / "apps/api-fsharp/tests/SalesManagement.Tests"
CATEGORY_PATTERN = re.compile(r'Trait\("Category",\s*"([^"]+)"\)')


def fail(message: str) -> None:
    print(f"[spring-contracts] FAIL: {message}", file=sys.stderr)
    raise SystemExit(1)


def operation_ids(text: str) -> list[str]:
    return re.findall(r"^\s+operationId:\s*([A-Za-z][A-Za-z0-9]*)\s*$", text, re.MULTILINE)


def fsharp_test_categories() -> set[str]:
    categories: set[str] = set()
    for source in FSHARP_TEST_ROOT.rglob("*.fs"):
        categories.update(CATEGORY_PATTERN.findall(source.read_text(encoding="utf-8")))
    return categories


def verify_test_category_ledger(ledger: dict[str, object]) -> None:
    required_fields = {
        "id",
        "fsharpCategory",
        "fsharpPurpose",
        "javaEvidence",
        "component",
    }
    entries = ledger.get("testCategories")
    if not isinstance(entries, list) or not entries:
        fail("パリティ台帳に F# テストカテゴリがありません")
    if any(not isinstance(entry, dict) or set(entry) != required_fields for entry in entries):
        fail("テストカテゴリ台帳の必須フィールドが欠落または増加しています")

    expected = fsharp_test_categories()
    actual = [entry["fsharpCategory"] for entry in entries]
    if len(actual) != len(set(actual)):
        fail("テストカテゴリ台帳に重複があります")
    if set(actual) != expected:
        missing = sorted(expected - set(actual))
        extra = sorted(set(actual) - expected)
        fail(f"F# テストカテゴリ台帳が不一致です: missing={missing}, extra={extra}")

    valid_components = {"api", "batch", "tools", "domain", "application", "infrastructure"}
    for entry in entries:
        if entry["component"] not in valid_components:
            fail(f"未知の component です: {entry['component']}")
        evidence_paths = str(entry["javaEvidence"]).split(";")
        if not evidence_paths or any(not (ROOT / path).is_file() for path in evidence_paths):
            fail(f"Java evidence がありません: {entry['id']}={entry['javaEvidence']}")


def main() -> None:
    if not OPENAPI.is_file() or not LEDGER.is_file() or not MIGRATION_ORDER.is_file():
        fail("共有契約、パリティ台帳、migration order のいずれかがありません")

    spec_operations = operation_ids(OPENAPI.read_text(encoding="utf-8"))
    ledger = json.loads(LEDGER.read_text(encoding="utf-8"))
    ledger_operations = ledger.get("operations")
    expected_count = ledger.get("expectedOperationCount")
    if len(spec_operations) != expected_count:
        fail(f"OpenAPI operation 数が {len(spec_operations)} 件です (期待: {expected_count})")
    if len(spec_operations) != len(set(spec_operations)):
        fail("OpenAPI の operationId が重複しています")
    if spec_operations != ledger_operations:
        missing = sorted(set(spec_operations) - set(ledger_operations or []))
        extra = sorted(set(ledger_operations or []) - set(spec_operations))
        fail(f"パリティ台帳が OpenAPI と不一致です: missing={missing}, extra={extra}")
    if set(ledger.get("components", [])) != {"api", "batch", "tools"}:
        fail("パリティ台帳は api / batch / tools の全 component を含む必要があります")
    verify_test_category_ledger(ledger)

    required_gate_fields = {
        "id",
        "fsharpPurpose",
        "javaTool",
        "target",
        "redFixture",
        "evidence",
    }
    gates = ledger.get("gates", [])
    if not gates or any(set(gate) != required_gate_fields for gate in gates):
        fail("gate 台帳の必須フィールドが欠落または増加しています")
    for gate in gates:
        fixtures = str(gate["redFixture"]).split(";")
        if not fixtures or any(not (ROOT / fixture).is_file() for fixture in fixtures):
            fail(f"Red fixture がありません: {gate['id']}={gate['redFixture']}")

    expected_migrations = [line for line in MIGRATION_ORDER.read_text().splitlines() if line]
    actual_migrations = sorted(path.name for path in MIGRATIONS.glob("*.sql"))
    if expected_migrations != actual_migrations:
        fail("Spring migration order が F# migration 一覧と不一致です")

    digest = hashlib.sha256(OPENAPI.read_bytes()).hexdigest()
    print(
        "[spring-contracts] OK: "
        f"operations={len(spec_operations)} migrations={len(actual_migrations)} "
        f"test-categories={len(fsharp_test_categories())} "
        f"openapi-sha256={digest}"
    )


if __name__ == "__main__":
    main()
