"""Schemathesis フック: ci.sh 経由で `SCHEMATHESIS_HOOKS=schemathesis-hooks.py` で読み込む。

Schemathesis は OpenAPI から個別 operation 単位でテストを生成するが、
`POST /sales-cases/{id}/contracts` のような **多段階の事前状態を要求するエンドポイント**
（例: 査定済み = appraised の sales case が必要）は、fuzzer が偶然に事前条件を満たすことが
ほぼ不可能。そのまま流すと "negative_data_rejection" 等で false positive を量産する。

`before_load_schema` フックで raw schema を直接書き換え、これらのオペレーションを
スキーマから物理的に取り除く。後段の operation discovery / strategy 生成では
当該オペレーションが存在しないものとして扱われる。

ここで除外するもの:
- /sales-cases/{id}/appraisals (POST/PUT/DELETE) — appraisal 作成自体は appraised 化フロー
- /sales-cases/{id}/contracts  (POST/DELETE)     — appraised 必須
- /sales-cases/{id}/shipping-* (POST/DELETE)     — contract 必須
- /sales-cases/{id}/reservation/*                — 予約フローの多段遷移
- /sales-cases/{id}/consignment/*                — 委託フローの多段遷移
- /lots/{id}/{action}                            — ロット状態遷移系（manufacturing→...→shipped）

ここで除外しないもの:
- GET /lots, GET /sales-cases (list)
- POST /lots, POST /sales-cases (create — 単独で完結)
- GET /lots/{id}, GET /sales-cases/{id} (read — 404 が想定内)
- DELETE /sales-cases/{id} (cascade 削除、単独で完結)
- /health, /auth/config, /api/external/* (副作用なし or 単発)
"""
from __future__ import annotations

from typing import Any

import schemathesis

# 状態遷移を要求するため fuzz から外すパステンプレート → 除外メソッド集合。
# `*` は当該パスのすべての HTTP メソッドを除外。
_STATEFUL_PATHS: dict[str, set[str]] = {
    "/sales-cases/{id}/appraisals": {"post", "put", "delete"},
    "/sales-cases/{id}/contracts": {"post", "delete"},
    "/sales-cases/{id}/shipping-instruction": {"post", "delete"},
    "/sales-cases/{id}/shipping-completion": {"post"},
    "/sales-cases/{id}/reservation/appraisals": {"post"},
    "/sales-cases/{id}/reservation/determine": {"post"},
    "/sales-cases/{id}/reservation/determination": {"delete"},
    "/sales-cases/{id}/reservation/delivery": {"post"},
    "/sales-cases/{id}/consignment/designate": {"post"},
    "/sales-cases/{id}/consignment/designation": {"delete"},
    "/sales-cases/{id}/consignment/result": {"post"},
    "/lots/{id}/complete-manufacturing": {"post"},
    "/lots/{id}/instruct-shipping": {"post"},
    "/lots/{id}/complete-shipping": {"post"},
    "/lots/{id}/cancel-manufacturing-completion": {"post"},
    "/lots/{id}/instruct-item-conversion": {"post", "delete"},
}


@schemathesis.hook
def before_load_schema(context: Any, raw_schema: dict[str, Any]) -> None:
    paths = raw_schema.get("paths") or {}
    removed: list[str] = []
    for template, methods in _STATEFUL_PATHS.items():
        node = paths.get(template)
        if not isinstance(node, dict):
            continue
        for method in list(methods):
            if method in node:
                node.pop(method, None)
                removed.append(f"{method.upper()} {template}")
        # メソッドが空になった場合はパスごと落とす
        if not any(k for k in node if k.lower() in {"get", "post", "put", "patch", "delete", "head", "options"}):
            paths.pop(template, None)
    if removed:
        print(f"[schemathesis-hooks] excluded {len(removed)} stateful operations: " + ", ".join(removed[:5]) + ("..." if len(removed) > 5 else ""))
