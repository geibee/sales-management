#!/usr/bin/env python3
"""Stop フック: merged.sarif を読み、頻出 ruleId を LESSONS.md に「教訓」として記録する。

挙動:
  1. apps/api-fsharp/ci-results/merged.sarif (CWD 相対) を読む。なければ exit 0
  2. ruleId を `<tool_name>.<ruleId>` でカウントし、最初に出会った message を保持
  3. 環境変数 SARIF_LESSONS_THRESHOLD (default=3) 以上の検出回数の rule を教訓として整形
  4. LESSONS.md のマーカ (<!-- lessons:begin --> / <!-- lessons:end -->) の間に記録する
     - 新規 rule: ツール別の対応ヒントを付けて追記
     - 既存 rule: 最終検出日と直近件数のみ更新 (本文は人間の蒸留編集を保持)
  5. エントリは最終検出日の降順で保持。SARIF_LESSONS_MAX (default=50) を超えた分は古い順に削除
  6. マーカの外 (ヘッダや人間が書いたメモ) には触れない
  7. `<!-- lessons:ignore `<key>` 理由 -->` 行のあるルールは記録しない (既存エントリも削除する)。
     「対応不要」という人間の判断を恒久記録し、削除した教訓の自動復活を防ぐ

メモリ品質の設計方針 (ループエンジニアリング):
  - ここは「未消化の教訓」の受け皿。恒久対応 (linter / ast-grep / verify スクリプト / スキーマ修正)
    が済んだ項目は人間または後続タスクが行ごと削除する
  - 追記のみで無限成長させない (上限 + 再発時は追記でなく更新)

CWD は Claude Code 起動時のリポジトリルートを想定。
"""
import json
import os
import pathlib
import re
from collections import Counter
from datetime import date

THRESHOLD = int(os.environ.get("SARIF_LESSONS_THRESHOLD", "3"))
MAX_ENTRIES = int(os.environ.get("SARIF_LESSONS_MAX", "50"))
SARIF_PATH = pathlib.Path("apps/api-fsharp/ci-results/merged.sarif")
LESSONS_PATH = pathlib.Path("LESSONS.md")

BEGIN_MARK = "<!-- lessons:begin -->"
END_MARK = "<!-- lessons:end -->"

# エントリ形式: - `<key>` — 最終検出 YYYY-MM-DD / 直近 N件: <本文>
ENTRY_RE = re.compile(
    r"^- `(?P<key>[^`]+)` — 最終検出 (?P<date>\d{4}-\d{2}-\d{2}) / 直近 (?P<count>\d+)件: (?P<body>.*)$"
)

# 人間のオーバーライド: この key は今後記録しない
# 例: <!-- lessons:ignore `OWASP ZAP.10104` User Agent Fuzzer はノイズと判断 (2026-07) -->
IGNORE_RE = re.compile(r"<!--\s*lessons:ignore\s+`([^`]+)`")

# ツール名 → 対応ヒント (生の検出結果を「次に取るべき行動」に変換する)
TOOL_HINTS = {
    "Schemathesis": "openapi.yaml のスキーマ制約・examples と API バリデーション実装のどちらが正か判断して修正。恒常的な誤検知は schemathesis-hooks.py で除外",
    "OWASP ZAP": "API 側の修正か zap-rules.tsv でのルール調整かを判断",
    "gitleaks": "漏えいした秘密情報は即ローテーション。誤検知のみ .gitleaks.toml の allowlist に追加",
    "Trivy": "renovate.json の優先度更新または依存の手動更新で対応",
    "FSharpLint": "lint ルールに従い修正。規約化できるものは fsharplint 設定に固定",
}

HEADER_TEMPLATE = """# 失敗から学んだこと (自動生成)

Stop フック (`.claude/scripts/sarif-to-lessons.py`) が `apps/api-fsharp/ci-results/merged.sarif` の
頻出ルール (検出数 ≥ SARIF_LESSONS_THRESHOLD, 既定 3) を下のマーカ間に記録する。マーカの外は人間の編集領域。

運用ルール:

- ここは「未消化の教訓」の受け皿。恒久対応 (linter / ast-grep / verify スクリプト / スキーマ修正) が済んだ項目は行ごと削除する
- 同じルールが再検出されたら日付と件数が更新される (再発の検知)。本文の手動編集 (蒸留) は保持される
- エントリ数が上限 (SARIF_LESSONS_MAX, 既定 50) を超えると、最終検出が古いものから削除される
- 対応不要と判断したルールは `<!-- lessons:ignore `<key>` 理由 -->` をマーカ外に書く。以後は再検出されても記録されない
- 追加・更新の履歴は本ファイルの git log で追跡する (別途の実行ログは持たない)

{begin}
{end}
"""


def load_results() -> tuple[Counter, dict[str, str]]:
    counts: Counter = Counter()
    samples: dict[str, str] = {}
    if not SARIF_PATH.exists():
        return counts, samples
    try:
        data = json.loads(SARIF_PATH.read_text())
    except json.JSONDecodeError:
        return counts, samples
    for run in data.get("runs", []) or []:
        tool = ((run.get("tool") or {}).get("driver") or {}).get("name", "?")
        for r in run.get("results", []) or []:
            rid = r.get("ruleId") or "?"
            key = f"{tool}.{rid}"
            counts[key] += 1
            if key not in samples:
                msg = ((r.get("message") or {}).get("text") or "")
                samples[key] = msg.replace("\n", " ").strip()[:120]
    return counts, samples


def hint_for(key: str) -> str:
    for tool, hint in TOOL_HINTS.items():
        if key.startswith(tool + "."):
            return hint
    return ""


def parse_entries(section: str) -> dict[str, dict]:
    """マーカ間のテキストを {key: {date, count, body}} に構造的にパースする。"""
    entries: dict[str, dict] = {}
    for line in section.splitlines():
        m = ENTRY_RE.match(line.strip())
        if m:
            entries[m.group("key")] = {
                "date": m.group("date"),
                "count": int(m.group("count")),
                "body": m.group("body"),
            }
    return entries


def render_entries(entries: dict[str, dict]) -> tuple[str, int]:
    ordered = sorted(entries.items(), key=lambda kv: kv[1]["date"], reverse=True)
    dropped = len(ordered) - MAX_ENTRIES
    if dropped > 0:
        ordered = ordered[:MAX_ENTRIES]
    lines = [
        f"- `{key}` — 最終検出 {e['date']} / 直近 {e['count']}件: {e['body']}"
        for key, e in ordered
    ]
    return "\n".join(lines), max(dropped, 0)


def main() -> int:
    counts, samples = load_results()
    if not counts:
        return 0

    today = date.today().isoformat()

    if LESSONS_PATH.exists():
        text = LESSONS_PATH.read_text()
        if BEGIN_MARK not in text or END_MARK not in text:
            suffix = "" if text.endswith("\n") else "\n"
            text = text + suffix + "\n" + BEGIN_MARK + "\n" + END_MARK + "\n"
    else:
        text = HEADER_TEMPLATE.format(begin=BEGIN_MARK, end=END_MARK)

    begin_idx = text.index(BEGIN_MARK) + len(BEGIN_MARK)
    end_idx = text.index(END_MARK)
    entries = parse_entries(text[begin_idx:end_idx])

    ignored = set(IGNORE_RE.findall(text))
    removed = 0
    for key in list(entries):
        if key in ignored:
            del entries[key]
            removed += 1

    added = updated = 0
    for key, n in counts.most_common():
        if n < THRESHOLD or key in ignored:
            continue
        if key in entries:
            if entries[key]["date"] != today or entries[key]["count"] != n:
                entries[key]["date"] = today
                entries[key]["count"] = n
                updated += 1
        else:
            msg = samples.get(key, "")
            hint = hint_for(key)
            body = f"{msg} → 対応: {hint}" if hint else msg
            entries[key] = {"date": today, "count": n, "body": body}
            added += 1

    if not (added or updated or removed):
        return 0

    rendered, dropped = render_entries(entries)
    section = "\n" + rendered + "\n" if rendered else "\n"
    LESSONS_PATH.write_text(text[:begin_idx] + section + text[end_idx:])
    note = f"[sarif-to-lessons] {LESSONS_PATH}: +{added} updated {updated} ignored-removed {removed}"
    if dropped:
        note += f" dropped {dropped} (max {MAX_ENTRIES})"
    print(note)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
