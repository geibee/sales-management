# dsl-parser

Sales Management DSL のパーサー。`dsl/grammar.ebnf` の **[CORE] サブセット**を AST に変換する。

## スコープ（HANDOFF P1-1）

現状は **`data <identifier> = <identifier>`** のみ受理する最小実装。
順次以下を追加していく（`dsl/grammar.ebnf` の章番号に対応）:

1. ✅ `data X = Y`（識別子のみ）
2. ⬜ AND（直積）
3. ⬜ OR（直和）
4. ⬜ `?`（オプショナル）
5. ⬜ `List<X>`
6. ⬜ `behavior` 宣言
7. ⬜ コメント末尾の `// 1件以上` 等の注釈収集

## セットアップ

```bash
# uv が必要
uv sync
```

## 使い方

```bash
# ファイルを指定
uv run dsl-parser path/to/input.dsl

# 標準入力から
echo 'data 事業部コード = 整数' | uv run dsl-parser -
```

出力は AST の JSON。

## テスト

```bash
uv run pytest
```

ゴールデンテストは `tests/golden/<case>/{input.dsl,expected.json}` 形式。
新ケースを追加するときは:

1. `tests/golden/NN-name/input.dsl` を作成
2. `uv run dsl-parser tests/golden/NN-name/input.dsl > tests/golden/NN-name/expected.json` で初版を生成
3. JSON を目視確認後コミット
