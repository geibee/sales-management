# Prompt: case-002 販売案件 TLA+ 生成

> **評価メソッド**: `sample-diff`。
> AI に渡す入力リスト・判定基準は `meta.md` を参照。

## 目的

`input.dsl`（販売案件ドメイン）から、状態と振る舞いを表現する TLA+ 仕様を
`generated.tla` として生成する。

## あなた（AI）が読むべき入力

1. **`input.dsl`**（このディレクトリ）— 販売案件領域の DSL
2. **`harness/SEMANTICS.md`** — 各構文の TLA+ 翻訳ルール
3. **`evaluation/case-001-inventory-lot/expected.tla`** — TLA+ のスタイル参照

## スコープと境界

- **case-002 の input.dsl にある宣言・behavior のみ**を対象とする
- 他ケースで定義される型は CONSTANTS で抽象的な集合として宣言
  例: `InventoryLots`, `Pricings`, `SalesContracts`, `ReservationPrices`
- DSL に内部構造の記載がない型（`委託業者情報` など）も同様に CONSTANTS

## 生成ルール

| DSL | TLA+ |
|---|---|
| `data X = A AND B` | `X == [a: A, b: B]` |
| `data X = A OR B` | バリアントごとにタグ付き record、`\cup` で和集合 |
| `AND F?` | `f: F \cup {NULL}` |
| `AND List<F>` // 1件以上 | `f: { s \in SUBSET F : Cardinality(s) >= 1 }` |
| `behavior B = I -> O` | アクション述語: 事前条件で入力状態を絞り、`salesCases'` で次状態を構築 |

## 状態モデル

- 単一の状態変数 `salesCases`（販売案件の集合）を持つ
- 各 behavior は `salesCases' = (salesCases \ {case_}) \cup {新案件}` の形で
  案件を置き換える
- `state` フィールドのタグで sum type のバリアントを識別

## 命名

- `直接販売案件` → `DirectSalesCase`、状態タグは `"PreAppraisalDirect"` など
- 衝突を避けるためサブドメイン名を含めたタグにする
  例: `"ConfirmedReservation"`（予約系）と `"AssignedConsignment"`（委託系）

## 出力

このディレクトリに `generated.tla` を作成。

## 注意点

- ファイル冒頭に `---- MODULE SalesCase ----`
- ファイル末尾に `====`
- `EXTENDS Naturals, FiniteSets` 程度

## 検証

```bash
bash evaluation/run.sh case-002-sales-case
```
