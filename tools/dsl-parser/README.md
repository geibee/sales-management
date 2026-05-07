# dsl-parser

Sales Management DSL のパーサー。`dsl/grammar.ebnf` の **[CORE] サブセット**を AST に変換する。

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
# .dsl ファイルを指定
uv run dsl-parser path/to/input.dsl

# .md ファイル（最初の fenced code block を抽出してパース）
uv run dsl-parser ../../dsl/domain-model.md

# 標準入力から
echo 'data 事業部コード = 整数' | uv run dsl-parser -
```

出力は AST の JSON。

## テスト

```bash
uv run pytest
```

- `tests/golden/<case>/{input.dsl,expected.json}` — パターン別のゴールデンテスト
- `tests/test_domain_model.py` — `dsl/domain-model.md` 全体のスモークテスト

新ゴールデンケースを追加するときは:

1. `tests/golden/NN-name/input.dsl` を作成
2. `uv run dsl-parser tests/golden/NN-name/input.dsl > tests/golden/NN-name/expected.json` で初版を生成
3. JSON を目視確認後コミット
