#!/usr/bin/env python3
"""ZAP API scan の JSON 出力 (-J zap-report.json) を SARIF v2.1.0 に変換する。

使い方:
    python3 scripts/zap-to-sarif.py <input.json> <output.sarif>
    python3 scripts/zap-to-sarif.py --execution-error <log> <output.sarif>

ZAP JSON 構造:
    site[].alerts[] に各アラートが入る。riskcode (0=Info, 1=Low, 2=Medium, 3=High)
    に応じて SARIF level (note/warning/warning/error) にマップする。
"""
import json
import pathlib
import sys


INFORMATION_URI = "https://www.zaproxy.org/"


def risk_to_level(riskcode: str | int) -> str:
    code = int(riskcode) if str(riskcode).isdigit() else 0
    if code >= 1:
        # ZAP の FAIL/WARN 判定は zap-api-scan.py の終了コードを正とする。
        # risk が High でもルール設定が WARN なら nightly を失敗させず、
        # SARIF / LESSONS で継続管理する。
        return "warning"
    return "note"


def write_execution_error(log_path: pathlib.Path, dst: pathlib.Path) -> int:
    try:
        log = log_path.read_text(errors="replace")
    except OSError as e:
        log = f"ZAP の実行ログを読み取れません: {e}"

    # SARIF と Actions の表示を壊さず、原因調査に必要な末尾を残す。
    excerpt = log[-4000:] if log else "ZAP レポートが生成されませんでした。"
    sarif = {
        "$schema": "https://json.schemastore.org/sarif-2.1.0.json",
        "version": "2.1.0",
        "runs": [{
            "tool": {
                "driver": {
                    "name": "OWASP ZAP",
                    "informationUri": INFORMATION_URI,
                    "rules": [{
                        "id": "ZAP.execution",
                        "name": "ZAP execution failure",
                        "shortDescription": {"text": "ZAP の実行またはレポート生成に失敗"},
                    }],
                }
            },
            "results": [{
                "ruleId": "ZAP.execution",
                "level": "error",
                "message": {"text": excerpt},
            }],
        }],
    }
    dst.parent.mkdir(parents=True, exist_ok=True)
    dst.write_text(json.dumps(sarif, indent=2, ensure_ascii=False))
    print(f"[zap-to-sarif] wrote execution error: {dst}")
    return 0


def main() -> int:
    if len(sys.argv) == 4 and sys.argv[1] == "--execution-error":
        return write_execution_error(pathlib.Path(sys.argv[2]), pathlib.Path(sys.argv[3]))

    if len(sys.argv) != 3:
        print(
            f"usage: {sys.argv[0]} <input.json> <output.sarif> | "
            f"{sys.argv[0]} --execution-error <log> <output.sarif>",
            file=sys.stderr,
        )
        return 2

    src = pathlib.Path(sys.argv[1])
    dst = pathlib.Path(sys.argv[2])

    if not src.exists():
        print(f"[zap-to-sarif] input not found: {src}", file=sys.stderr)
        return 1

    try:
        data = json.loads(src.read_text())
    except json.JSONDecodeError as e:
        print(f"[zap-to-sarif] invalid JSON: {e}", file=sys.stderr)
        return 1

    rules: dict[str, dict] = {}
    results: list[dict] = []

    for site in data.get("site", []):
        host = site.get("@name", "")
        for alert in site.get("alerts", []):
            rule_id = str(alert.get("pluginid") or alert.get("alertRef") or alert.get("alert"))
            if rule_id and rule_id not in rules:
                rules[rule_id] = {
                    "id": rule_id,
                    "name": alert.get("name") or rule_id,
                    "shortDescription": {"text": alert.get("name", "")[:200]},
                    "fullDescription": {"text": (alert.get("desc") or "")[:1000]},
                    "helpUri": (alert.get("reference") or "").split("\n")[0],
                }

            for inst in alert.get("instances", []) or [{}]:
                uri = inst.get("uri") or host or "unknown"
                results.append({
                    "ruleId": rule_id,
                    "level": risk_to_level(alert.get("riskcode", 0)),
                    "message": {"text": (alert.get("name") or "") + ": " + (inst.get("evidence") or alert.get("desc") or "")[:300]},
                    "locations": [{
                        "physicalLocation": {
                            "artifactLocation": {"uri": uri},
                            "region": {"startLine": 1},
                        }
                    }],
                })

    sarif = {
        "$schema": "https://json.schemastore.org/sarif-2.1.0.json",
        "version": "2.1.0",
        "runs": [{
            "tool": {
                "driver": {
                    "name": "OWASP ZAP",
                    "version": str(data.get("@version", "")),
                    "informationUri": INFORMATION_URI,
                    "rules": list(rules.values()),
                }
            },
            "results": results,
        }],
    }

    dst.parent.mkdir(parents=True, exist_ok=True)
    dst.write_text(json.dumps(sarif, indent=2, ensure_ascii=False))
    print(f"[zap-to-sarif] wrote {dst} ({len(results)} results, {len(rules)} rules)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
