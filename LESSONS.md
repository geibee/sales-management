# 失敗から学んだこと (自動生成)

Stop フック (`.claude/scripts/sarif-to-lessons.py`) が `apps/api-fsharp/ci-results/merged.sarif` の
頻出ルール (検出数 ≥ SARIF_LESSONS_THRESHOLD, 既定 3) を下のマーカ間に記録する。マーカの外は人間の編集領域。

運用ルール:

- ここは「未消化の教訓」の受け皿。恒久対応 (linter / ast-grep / verify スクリプト / スキーマ修正) が済んだ項目は行ごと削除する
- 同じルールが再検出されたら日付と件数が更新される (再発の検知)。本文の手動編集 (蒸留) は保持される
- エントリ数が上限 (SARIF_LESSONS_MAX, 既定 50) を超えると、最終検出が古いものから削除される
- 対応不要と判断したルールは `<!-- lessons:ignore `<key>` 理由 -->` をマーカ外に書く。以後は再検出されても記録されない
- 追加・更新の履歴は本ファイルの git log で追跡する (別途の実行ログは持たない)

<!-- lessons:begin -->
- `OWASP ZAP.100000` — 最終検出 2026-07-08 / 直近 311件: A Client Error response code was returned by the server: 400 → 対応: API 側の修正か zap-rules.tsv でのルール調整かを判断
- `OWASP ZAP.10049` — 最終検出 2026-07-08 / 直近 9件: Non-Storable Content: 400 → 対応: API 側の修正か zap-rules.tsv でのルール調整かを判断
- `OWASP ZAP.10104` — 最終検出 2026-07-08 / 直近 5件: User Agent Fuzzer: <p>Check for differences in response based on fuzzed User Agent (eg. mobile sites, access as a Search → 対応: API 側の修正か zap-rules.tsv でのルール調整かを判断
- `Schemathesis.skipped` — 最終検出 2026-07-08 / 直近 11件: POST /lots: No examples in schema → 対応: openapi.yaml のスキーマ制約・examples と API バリデーション実装のどちらが正か判断して修正。恒常的な誤検知は schemathesis-hooks.py で除外
<!-- lessons:end -->
