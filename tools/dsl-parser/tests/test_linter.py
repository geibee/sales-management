"""spec.json と F# Domain 実装の最小照合リンターテスト。"""

from __future__ import annotations

from pathlib import Path

from dsl_parser.linter import lint_spec_against_fsharp


def _spec() -> dict[str, object]:
    return {
        "schemaVersion": 1,
        "data": [
            {
                "name": "在庫ロット",
                "kind": "or",
                "variants": [
                    {
                        "kind": "named",
                        "name": "製造中ロット",
                        "optional": False,
                        "list": False,
                    },
                    {
                        "kind": "named",
                        "name": "製造完了ロット",
                        "optional": False,
                        "list": False,
                    },
                ],
            }
        ],
        "behaviors": [
            {
                "name": "製造完了を指示する",
                "input": {
                    "kind": "named",
                    "name": "製造中ロット",
                    "optional": False,
                    "list": False,
                },
                "output": {
                    "kind": "named",
                    "name": "製造完了ロット",
                    "optional": False,
                    "list": False,
                },
                "error": {
                    "kind": "named",
                    "name": "製造完了指示エラー",
                    "optional": False,
                    "list": False,
                },
            }
        ],
    }


def _write_glossary(path: Path) -> None:
    path.write_text(
        "\n".join(
            [
                "在庫ロット: InventoryLot",
                "製造中ロット: ManufacturingLot",
                "製造完了ロット: ManufacturedLot",
                "製造完了を指示する: completeManufacturing",
            ]
        )
        + "\n",
        encoding="utf-8",
    )


def test_linter_accepts_matching_inventory_lot_implementation(tmp_path: Path) -> None:
    domain_dir = tmp_path / "Domain"
    domain_dir.mkdir()
    glossary_path = tmp_path / "glossary.yaml"
    _write_glossary(glossary_path)

    (domain_dir / "Types.fs").write_text(
        """
module SalesManagement.Domain.Types

type ManufacturingLot = { Common: string }
type ManufacturedLot = { Common: string }

type InventoryLot =
    | Manufacturing of ManufacturingLot
    | Manufactured of ManufacturedLot
""".strip()
        + "\n",
        encoding="utf-8",
    )
    (domain_dir / "LotWorkflows.fs").write_text(
        """
module SalesManagement.Domain.LotWorkflows

let completeManufacturing date lot = lot
""".strip()
        + "\n",
        encoding="utf-8",
    )

    findings = lint_spec_against_fsharp(_spec(), domain_dir, glossary_path)

    assert findings == []


def test_linter_reports_missing_inventory_lot_variant(tmp_path: Path) -> None:
    domain_dir = tmp_path / "Domain"
    domain_dir.mkdir()
    glossary_path = tmp_path / "glossary.yaml"
    _write_glossary(glossary_path)

    (domain_dir / "Types.fs").write_text(
        """
module SalesManagement.Domain.Types

type ManufacturingLot = { Common: string }
type ManufacturedLot = { Common: string }

type InventoryLot =
    | Manufacturing of ManufacturingLot
""".strip()
        + "\n",
        encoding="utf-8",
    )
    (domain_dir / "LotWorkflows.fs").write_text(
        "module SalesManagement.Domain.LotWorkflows\n\nlet completeManufacturing date lot = lot\n",
        encoding="utf-8",
    )

    findings = lint_spec_against_fsharp(_spec(), domain_dir, glossary_path)

    assert [f.code for f in findings] == ["missing-union-variant"]
    assert "ManufacturedLot" in findings[0].message


def test_linter_reports_missing_behavior_function(tmp_path: Path) -> None:
    domain_dir = tmp_path / "Domain"
    domain_dir.mkdir()
    glossary_path = tmp_path / "glossary.yaml"
    _write_glossary(glossary_path)

    (domain_dir / "Types.fs").write_text(
        """
module SalesManagement.Domain.Types

type ManufacturingLot = { Common: string }
type ManufacturedLot = { Common: string }

type InventoryLot =
    | Manufacturing of ManufacturingLot
    | Manufactured of ManufacturedLot
""".strip()
        + "\n",
        encoding="utf-8",
    )
    (domain_dir / "LotWorkflows.fs").write_text(
        "module SalesManagement.Domain.LotWorkflows\n",
        encoding="utf-8",
    )

    findings = lint_spec_against_fsharp(_spec(), domain_dir, glossary_path)

    assert [f.code for f in findings] == ["missing-behavior-function"]
    assert "completeManufacturing" in findings[0].message
