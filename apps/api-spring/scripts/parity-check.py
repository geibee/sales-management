#!/usr/bin/env python3
"""F# と Spring に同じ 35 operation シナリオを与え、HTTP と DB 副作用を比較する。"""

from __future__ import annotations

import argparse
import base64
import json
import subprocess
import urllib.error
import urllib.request
from pathlib import Path
from typing import Any, NamedTuple


ROOT = Path(__file__).resolve().parents[1]
IMPORTANT_HEADERS = (
    "content-type",
    "content-disposition",
    "allow",
    "retry-after",
    "x-cache",
    "x-content-type-options",
    "x-frame-options",
    "x-xss-protection",
    "content-security-policy",
    "referrer-policy",
    "cross-origin-resource-policy",
)
SNAPSHOT_TABLES = {
    "lot": ("created_at", "updated_at"),
    "lot_detail": (),
    "sales_case": (),
    "sales_case_lot": (),
    "appraisal": (),
    "contract": (),
    "lot_appraisal": (),
    "lot_detail_appraisal": (),
    "reservation_price": (),
    "consignment_info": (),
    "consignment_result": (),
    "outbox_events": ("created_at", "processed_at", "status", "error_detail"),
}


class Response(NamedTuple):
    status: int
    headers: dict[str, str]
    normalized_body: str
    parsed_body: Any

    def audit_value(self) -> dict[str, Any]:
        return {
            "status": self.status,
            "headers": self.headers,
            "body": self.normalized_body,
        }


def normalize_body(content_type: str, raw: bytes) -> tuple[str, Any]:
    if not raw:
        return "", None
    if "json" in content_type:
        parsed = json.loads(raw)
        return json.dumps(parsed, ensure_ascii=False, sort_keys=True, separators=(",", ":")), parsed
    return base64.b64encode(raw).decode("ascii"), raw


def normalize_header(name: str, value: str) -> str:
    if name == "content-type":
        return ";".join(part.strip().lower() for part in value.split(";"))
    return value


def request(base_url: str, method: str, path: str, body: dict[str, Any] | None) -> Response:
    raw_body = None if body is None else json.dumps(body, ensure_ascii=False, separators=(",", ":")).encode()
    no_content_delete = method == "DELETE" and (
        (path.startswith("/sales-cases/") and path.count("/") == 2)
        or path.endswith(("/appraisals", "/contracts", "/shipping-instruction"))
    )
    accept = "text/csv" if path.startswith("/lots/export") else "*/*" if no_content_delete else "application/json"
    headers = {"Accept": accept}
    if raw_body is not None:
        headers["Content-Type"] = "application/json"
    outgoing = urllib.request.Request(
        f"{base_url.rstrip('/')}{path}", data=raw_body, headers=headers, method=method
    )
    try:
        received = urllib.request.urlopen(outgoing, timeout=10)
    except urllib.error.HTTPError as error:
        received = error
    with received:
        raw = received.read()
        response_headers = {
            name: normalize_header(name, received.headers[name])
            for name in IMPORTANT_HEADERS
            if received.headers.get(name) is not None
        }
        content_type = received.headers.get("content-type", "")
        normalized, parsed = normalize_body(content_type, raw)
        return Response(received.status, response_headers, normalized, parsed)


class ParityScenario:
    def __init__(self, fsharp_url: str, spring_url: str, output: Path):
        self.fsharp_url = fsharp_url
        self.spring_url = spring_url
        self.output = output
        self.observed_operations: set[str] = set()
        self.transcript: list[dict[str, Any]] = []

    def call(
        self,
        operation: str,
        method: str,
        path: str,
        body: dict[str, Any] | None = None,
    ) -> Response:
        fsharp = request(self.fsharp_url, method, path, body)
        spring = request(self.spring_url, method, path, body)
        entry = {
            "operationId": operation,
            "request": {"method": method, "path": path, "body": body},
            "fsharp": fsharp.audit_value(),
            "spring": spring.audit_value(),
        }
        self.transcript.append(entry)
        self.write_transcript()
        if fsharp.audit_value() != spring.audit_value():
            raise AssertionError(
                f"{operation} の HTTP parity 不一致:\n"
                + json.dumps(entry, ensure_ascii=False, indent=2)
            )
        self.observed_operations.add(operation)
        return fsharp

    def write_transcript(self) -> None:
        self.output.parent.mkdir(parents=True, exist_ok=True)
        self.output.write_text(
            json.dumps({"requests": self.transcript}, ensure_ascii=False, indent=2),
            encoding="utf-8",
        )

    def create_lot(self, sequence: int) -> str:
        response = self.call(
            "createLot",
            "POST",
            "/lots",
            {
                "lotNumber": {"year": 2098, "location": "PARITY", "seq": sequence},
                "divisionCode": 1,
                "departmentCode": 10,
                "sectionCode": 100,
                "processCategory": 1,
                "inspectionCategory": 1,
                "manufacturingCategory": 1,
                "details": [
                    {
                        "itemCategory": "premium",
                        "premiumCategory": "A",
                        "productCategoryCode": "P1",
                        "lengthSpecLower": 1.0,
                        "thicknessSpecLower": 1.0,
                        "thicknessSpecUpper": 2.0,
                        "qualityGrade": "A",
                        "count": 1,
                        "quantity": 10.0,
                        "inspectionResultCategory": "pass",
                    }
                ],
            },
        )
        return str(response.parsed_body["lotNumber"])

    def complete_lot(self, lot: str, version: int = 1) -> Response:
        return self.call(
            "completeManufacturing",
            "POST",
            f"/lots/{lot}/complete-manufacturing",
            {"date": "2098-01-10", "version": version},
        )

    def create_case(self, case_type: str, lot: str) -> Response:
        return self.call(
            "createSalesCase",
            "POST",
            "/sales-cases",
            {
                "lots": [lot],
                "divisionCode": 1,
                "salesDate": "2098-01-15",
                "caseType": case_type,
            },
        )

    def get_case(self, case_id: str) -> Response:
        return self.call("getSalesCase", "GET", f"/sales-cases/{case_id}")

    def run(self) -> None:
        self.call("healthCheck", "GET", "/health")
        self.call("getAuthConfig", "GET", "/auth/config")
        self.call("listLots", "GET", "/lots?limit=50&offset=0")

        shipping_lot = self.create_lot(1)
        self.call("exportLotsCsv", "GET", "/lots/export?format=csv")
        self.call("listAvailableLots", "GET", "/lots/available")
        self.call("getCodeMasters", "GET", "/code-masters")
        self.call("getLot", "GET", f"/lots/{shipping_lot}")
        manufactured = self.complete_lot(shipping_lot)
        instructed = self.call(
            "instructLotShipping",
            "POST",
            f"/lots/{shipping_lot}/instruct-shipping",
            {"deadline": "2098-02-01", "version": manufactured.parsed_body["version"]},
        )
        self.call(
            "completeLotShipping",
            "POST",
            f"/lots/{shipping_lot}/complete-shipping",
            {"date": "2098-02-02", "version": instructed.parsed_body["version"]},
        )

        cancelled_lot = self.create_lot(2)
        cancelled_manufacturing = self.complete_lot(cancelled_lot)
        self.call(
            "cancelManufacturingCompletion",
            "POST",
            f"/lots/{cancelled_lot}/cancel-manufacturing-completion",
            {"version": cancelled_manufacturing.parsed_body["version"]},
        )

        conversion_lot = self.create_lot(3)
        conversion_manufactured = self.complete_lot(conversion_lot)
        conversion = self.call(
            "instructItemConversion",
            "POST",
            f"/lots/{conversion_lot}/instruct-item-conversion",
            {"destinationItem": "変換先品目", "version": conversion_manufactured.parsed_body["version"]},
        )
        self.call(
            "cancelItemConversionInstruction",
            "DELETE",
            f"/lots/{conversion_lot}/instruct-item-conversion",
            {"version": conversion.parsed_body["version"]},
        )

        sales_lots = []
        for sequence in range(4, 10):
            lot = self.create_lot(sequence)
            self.complete_lot(lot)
            sales_lots.append(lot)

        self.call("listSalesCases", "GET", "/sales-cases?limit=50&offset=0")
        direct = self.create_case("direct", sales_lots[0])
        direct_id = str(direct.parsed_body["salesCaseNumber"])
        edited = self.call(
            "editCaseLots",
            "PUT",
            f"/sales-cases/{direct_id}/lots",
            {"lots": [sales_lots[1]], "version": direct.parsed_body["version"]},
        )
        self.get_case(direct_id)

        deleted_case = self.create_case("direct", sales_lots[2])
        self.call(
            "deleteSalesCase",
            "DELETE",
            f"/sales-cases/{deleted_case.parsed_body['salesCaseNumber']}",
            {"version": deleted_case.parsed_body["version"]},
        )

        appraisal_body = self.appraisal_body(sales_lots[1], edited.parsed_body["version"])
        appraised = self.call(
            "createSalesAppraisal", "POST", f"/sales-cases/{direct_id}/appraisals", appraisal_body
        )
        appraisal_body["taxExcludedEstimatedTotal"] = 110000
        appraisal_body["version"] = appraised.parsed_body["version"]
        updated = self.call(
            "updateSalesAppraisal", "PUT", f"/sales-cases/{direct_id}/appraisals", appraisal_body
        )
        self.call(
            "deleteSalesAppraisal",
            "DELETE",
            f"/sales-cases/{direct_id}/appraisals",
            {"version": updated.parsed_body["version"]},
        )
        after_appraisal_delete = self.get_case(direct_id)
        appraisal_body["version"] = after_appraisal_delete.parsed_body["version"]
        appraised_again = self.call(
            "createSalesAppraisal", "POST", f"/sales-cases/{direct_id}/appraisals", appraisal_body
        )

        contract_body = self.contract_body(appraised_again.parsed_body["version"])
        contracted = self.call(
            "createSalesContract", "POST", f"/sales-cases/{direct_id}/contracts", contract_body
        )
        self.call(
            "deleteSalesContract",
            "DELETE",
            f"/sales-cases/{direct_id}/contracts",
            {"version": contracted.parsed_body["version"]},
        )
        after_contract_delete = self.get_case(direct_id)
        contract_body["version"] = after_contract_delete.parsed_body["version"]
        contracted_again = self.call(
            "createSalesContract", "POST", f"/sales-cases/{direct_id}/contracts", contract_body
        )
        case_shipping = self.call(
            "instructSalesCaseShipping",
            "POST",
            f"/sales-cases/{direct_id}/shipping-instruction",
            {"date": "2098-02-10", "version": contracted_again.parsed_body["version"]},
        )
        self.call(
            "cancelSalesCaseShippingInstruction",
            "DELETE",
            f"/sales-cases/{direct_id}/shipping-instruction",
            {"version": case_shipping.parsed_body["version"]},
        )
        after_shipping_cancel = self.get_case(direct_id)
        case_shipping_again = self.call(
            "instructSalesCaseShipping",
            "POST",
            f"/sales-cases/{direct_id}/shipping-instruction",
            {"date": "2098-02-10", "version": after_shipping_cancel.parsed_body["version"]},
        )
        self.call(
            "completeSalesCaseShipping",
            "POST",
            f"/sales-cases/{direct_id}/shipping-completion",
            {"date": "2098-02-20", "version": case_shipping_again.parsed_body["version"]},
        )

        reservation = self.create_case("reservation", sales_lots[3])
        reservation_id = str(reservation.parsed_body["salesCaseNumber"])
        reservation_price = self.call(
            "createReservationPrice",
            "POST",
            f"/sales-cases/{reservation_id}/reservation/appraisals",
            {
                "appraisalDate": "2098-01-20",
                "reservedLotInfo": "reserved-info",
                "reservedAmount": 500000,
                "version": reservation.parsed_body["version"],
            },
        )
        determination = self.call(
            "confirmReservation",
            "POST",
            f"/sales-cases/{reservation_id}/reservation/determine",
            {
                "determinedDate": "2098-01-22",
                "determinedAmount": 480000,
                "version": reservation_price.parsed_body["version"],
            },
        )
        cancelled = self.call(
            "cancelReservationConfirmation",
            "DELETE",
            f"/sales-cases/{reservation_id}/reservation/determination",
            {"version": determination.parsed_body["version"]},
        )
        determination_again = self.call(
            "confirmReservation",
            "POST",
            f"/sales-cases/{reservation_id}/reservation/determine",
            {
                "determinedDate": "2098-01-22",
                "determinedAmount": 480000,
                "version": cancelled.parsed_body["version"],
            },
        )
        self.call(
            "deliverReservation",
            "POST",
            f"/sales-cases/{reservation_id}/reservation/delivery",
            {"deliveryDate": "2098-01-30", "version": determination_again.parsed_body["version"]},
        )

        consignment = self.create_case("consignment", sales_lots[4])
        consignment_id = str(consignment.parsed_body["salesCaseNumber"])
        designated = self.call(
            "designateConsignment",
            "POST",
            f"/sales-cases/{consignment_id}/consignment/designate",
            {
                "consignorName": "Acme",
                "consignorCode": "ACME",
                "designatedDate": "2098-01-25",
                "version": consignment.parsed_body["version"],
            },
        )
        designation_cancelled = self.call(
            "cancelConsignmentDesignation",
            "DELETE",
            f"/sales-cases/{consignment_id}/consignment/designation",
            {"version": designated.parsed_body["version"]},
        )
        designated_again = self.call(
            "designateConsignment",
            "POST",
            f"/sales-cases/{consignment_id}/consignment/designate",
            {
                "consignorName": "Acme",
                "consignorCode": "ACME",
                "designatedDate": "2098-01-25",
                "version": designation_cancelled.parsed_body["version"],
            },
        )
        self.call(
            "registerConsignmentResult",
            "POST",
            f"/sales-cases/{consignment_id}/consignment/result",
            {
                "resultDate": "2098-01-30",
                "resultAmount": 480000,
                "version": designated_again.parsed_body["version"],
            },
        )

        self.call("externalPriceCheck", "GET", f"/api/external/price-check?lotId={sales_lots[5]}")

    @staticmethod
    def appraisal_body(lot: str, version: int) -> dict[str, Any]:
        return {
            "type": "normal",
            "appraisalDate": "2098-01-20",
            "deliveryDate": "2098-01-25",
            "salesMarket": "Tokyo",
            "baseUnitPriceDate": "2098-01-01",
            "periodAdjustmentRateDate": "2098-01-01",
            "counterpartyAdjustmentRateDate": "2098-01-01",
            "taxExcludedEstimatedTotal": 100000,
            "lotAppraisals": [
                {
                    "lotNumber": lot,
                    "detailAppraisals": [
                        {
                            "detailIndex": 1,
                            "baseUnitPrice": 1000,
                            "periodAdjustmentRate": 1.0,
                            "counterpartyAdjustmentRate": 1.0,
                        }
                    ],
                }
            ],
            "version": version,
        }

    @staticmethod
    def contract_body(version: int) -> dict[str, Any]:
        return {
            "contractDate": "2098-02-01",
            "person": "person",
            "buyer": {"customerNumber": "C001", "agentName": "agent"},
            "salesType": 1,
            "item": "item",
            "deliveryMethod": "delivery",
            "paymentDeferralCondition": "",
            "salesMethod": 1,
            "usage": "",
            "taxExcludedContractAmount": 100000,
            "consumptionTax": 10000,
            "taxExcludedPaymentAmount": 100000,
            "paymentConsumptionTax": 10000,
            "version": version,
        }


def database_snapshot(database_url: str) -> dict[str, Any]:
    snapshot: dict[str, Any] = {}
    for table, excluded_columns in SNAPSHOT_TABLES.items():
        expression = "to_jsonb(row_value)"
        for column in excluded_columns:
            expression += f" - '{column}'"
        query = (
            "SELECT COALESCE(jsonb_agg(row_data ORDER BY row_data::text), '[]'::jsonb)::text "
            f"FROM (SELECT {expression} AS row_data FROM {table} AS row_value) AS snapshot"
        )
        completed = subprocess.run(
            ["psql", database_url, "-X", "-A", "-t", "-v", "ON_ERROR_STOP=1", "-c", query],
            check=True,
            capture_output=True,
            text=True,
        )
        snapshot[table] = json.loads(completed.stdout.strip())
    return snapshot


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--fsharp-url", required=True)
    parser.add_argument("--spring-url", required=True)
    parser.add_argument("--fsharp-database-url", required=True)
    parser.add_argument("--spring-database-url", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()

    output = Path(args.output)
    scenario = ParityScenario(args.fsharp_url, args.spring_url, output)
    scenario.run()

    expected_operations = set(json.loads((ROOT / "parity-ledger.json").read_text())["operations"])
    if scenario.observed_operations != expected_operations:
        missing = sorted(expected_operations - scenario.observed_operations)
        extra = sorted(scenario.observed_operations - expected_operations)
        raise AssertionError(f"operation coverage 不一致: missing={missing}, extra={extra}")

    fsharp_database = database_snapshot(args.fsharp_database_url)
    spring_database = database_snapshot(args.spring_database_url)
    scenario.write_transcript()
    document = json.loads(output.read_text(encoding="utf-8"))
    document["operationCoverage"] = sorted(scenario.observed_operations)
    document["databases"] = {"fsharp": fsharp_database, "spring": spring_database}
    output.write_text(json.dumps(document, ensure_ascii=False, indent=2), encoding="utf-8")
    if fsharp_database != spring_database:
        raise AssertionError("DB parity 不一致。parity transcript の databases を確認してください")
    print(f"[spring-parity] OK: operations={len(scenario.observed_operations)} DB tables={len(SNAPSHOT_TABLES)}")


if __name__ == "__main__":
    main()
