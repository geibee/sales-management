# Prompt: case-003 価格査定・販売契約 TLA+ 生成

> **評価メソッド**: `sample-diff`。
> AI に渡す入力リスト・判定基準は `meta.md` を参照。

## 目的

`input.dsl`（価格査定・販売契約）から、状態と振る舞いを表現する TLA+ 仕様を
`generated.tla` として生成する。

## あなた（AI）が読むべき入力

1. **`input.dsl`**（このディレクトリ）— 価格査定・販売契約領域の DSL
2. **`harness/SEMANTICS.md`** — 各構文の TLA+ 翻訳ルール
3. **`evaluation/case-001-inventory-lot/expected.tla`** — TLA+ のスタイル参照

## スコープと境界

- case-003 input.dsl にある宣言・behavior のみが対象
- 他ケースで定義される型は CONSTANTS で抽象的に宣言:
  `LotNumbers`, `LotItems`, `PreAppraisalDirectSalesCases`,
  `AppraisedDirectSalesCases`, `ContractedDirectSalesCases`
- DSL に内部構造の記載がない型（`査定情報`, `契約情報`, `販売市場` 等）も
  CONSTANTS

## 状態モデル

- 単一の状態変数 `salesCases`（直接販売案件の集合）を持つ
- behavior は `salesCases' = (salesCases \ {case_}) \cup {新案件}` の形で
  案件を 査定前 ↔ 査定済み ↔ 契約済み の間で遷移させる

## 多数のオプショナルフィールド

`LotPricing` は 9 個の `?` フィールドを持つ:
- `MachiningCost \cup {NULL}` のように TLA+ で表現
- 数値型は `Nat` を流用してよい（業務上の精度は decimal だが Int 抽象で十分）

## 名前

- `通常査定` のタグは `"Standard"`
- `顧客契約査定` のタグは `"CustomerContract"`
- `暫定予約価格` のタグは `"Tentative"`
- `確定予約価格` のタグは `"Confirmed"`

## 出力

このディレクトリに `generated.tla` を作成。

## 注意点

- ファイル冒頭に `---- MODULE Pricing ----`
- ファイル末尾に `====`

## 検証

```bash
bash evaluation/run.sh case-003-pricing
```
