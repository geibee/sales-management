# case-001 在庫ロット — メタ情報

## 領域

在庫ロットドメイン。`dsl/domain-model.md` の在庫ロット部分を抽出した
`input.dsl` が AI への入力。

## 評価メソッド

| メソッド | 内容 |
|---|---|
| `sample-diff` | `expected.fs` / `expected.mmd` / `expected.als` と diff を取る |
| `compile-check` | `generated.fs` が F# として通るか確認（dotnet があれば自動） |

**注**: `expected.fs` は `harness/reference/InventoryLot.fs` の流用。
リファレンスは「**翻訳スタイルのサンプル**」であって厳格なゴールド標準
ではないため、`sample-diff` の差分は **完全一致を要求しない**。

## AI に渡す入力

- `dsl/grammar.ebnf`（[CORE] サブセット）
- `harness/SEMANTICS.md`（[CORE] / [CORE 派生ビュー] 章）
- `harness/reference/InventoryLot.fs`（**スタイルサンプル**として）
- `evaluation/case-001-inventory-lot/input.dsl`
- `evaluation/case-001-inventory-lot/prompt-<target>.md`

## 判定基準

| ターゲット | 基準 |
|---|---|
| F# (`generated.fs`) | サンプルと意味論的に等価。型構造・型名・フィールド名・関数シグネチャは概ね一致を期待。順序・コメント・docstring 文言の差は許容。`compile-check` を別途必須 |
| Mermaid (`generated.mmd`) | サンプルと状態名・遷移ラベル・初期/終端状態の整合が取れていること |
| Alloy (`generated.als`) | sig 構造・extends 関係・pred シグネチャが一致。`fact` の値域制約は `Amount`, `Count` のみ要求。`run` コマンドの順序・サイズ指定は許容 |

## このケースで検出したいこと（評価対象 A, D）

- AI 生成パイプラインが健全に動いている（A. 健全性）
- DSL / SEMANTICS / reference の変更で出力が大きく壊れていない（D. 回帰検出）

## 検出しにくいこと

- `SEMANTICS.md` の不足や曖昧さ（B. 規則の十分性）
- AI が rule を一般化して未知領域に適用する能力（B, C）

これらは `case-002`（販売案件）以降の `expert-review` メソッドで補完する。
