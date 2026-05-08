# dsl-parser

Sales Management DSL のパーサー。`dsl/grammar.ebnf` の **[CORE] サブセット**を AST または照合向け `spec.json` に変換する。

## スコープ

現状は **[CORE] 全機能**を受理する。`dsl/domain-model.md` 全体をパース可能。

| 構文 | 状態 |
|---|---|
| `data X = Y`（識別子のみ） | ✅ |
| AND（直積） | ✅ |
| OR（直和、AND > OR の優先順位） | ✅ |
| `?`（オプショナル） | ✅ |
| `List<X>` | ✅ |
| `( ... )`（グルーピング） | ✅ |
| `behavior X = Input -> Output OR Error` | ✅ |
| 行コメント `// ...` | ✅（スキップ） |
| `where` / `requires` / `ensures` 等 [VERIFICATION] 層 | ⬜（P4） |

## セットアップ

```bash
# uv が必要
uv sync
```

## 使い方

```bash
# .dsl ファイルを指定（既存互換: AST JSON）
uv run dsl-parser path/to/input.dsl

# 照合向け spec JSON
uv run dsl-parser --format spec ../../dsl/domain-model.md > /tmp/sales-management-spec.json

# .md ファイル（最初の fenced code block を抽出してパース）
uv run dsl-parser ../../dsl/domain-model.md

# 標準入力から
echo 'data 事業部コード = 整数' | uv run dsl-parser -
```

既定の出力は AST の JSON。`--format spec` を指定すると、`data` 宣言と `behavior` 宣言を照合しやすい正規化形式で出力する。`List<X> // 1件以上` は `list: true` と `minItems: 1` として spec に残す。

## F# 実装照合リンター

初手のリンターはコード生成を行わず、`spec.json` と `apps/api-fsharp/src/SalesManagement/Domain/*.fs` を静的テキスト/正規表現で照合する。

```bash
uv run dsl-parser --format spec ../../dsl/domain-model.md > /tmp/sales-management-spec.json
uv run dsl-spec-lint \
  --spec /tmp/sales-management-spec.json \
  --domain-dir ../../apps/api-fsharp/src/SalesManagement/Domain \
  --glossary ../../glossary.yaml
```

照合対象の英訳はリポジトリ直下の `glossary.yaml` から読む。現時点では在庫ロット領域の主要OR型、variant、behavior関数だけを対象にする。

リンターは `checkedTypes`, `checkedBehaviors`, `skippedDueToMissingGlossary` を出力する。辞書登録済みの親OR型配下に未登録variantがある場合と、input/outputが辞書登録済み型だけで構成される未登録behaviorは `missing-glossary-entry` として失敗させる。

## テスト

```bash
uv run pytest

# spec生成テストとリンターまで含む軽量CI
bash ci.sh
```

- `tests/golden/<case>/{input.dsl,expected.json}` — パターン別のゴールデンテスト
- `tests/spec_golden/<case>/{input.dsl,expected.json}` — spec JSON のゴールデンテスト
- `tests/test_domain_model.py` — `dsl/domain-model.md` 全体のスモークテスト
- `tests/test_linter.py` — spec/F# 実装照合リンターの最小テスト

新ゴールデンケースを追加するときは:

1. `tests/golden/NN-name/input.dsl` を作成
2. `uv run dsl-parser tests/golden/NN-name/input.dsl > tests/golden/NN-name/expected.json` で初版を生成
3. JSON を目視確認後コミット
