# Prompt: case-003 価格査定・販売契約 Alloy 生成

> **評価メソッド**: `sample-diff`。
> AI に渡す入力リスト・判定基準は `meta.md` を参照。

## 目的

`input.dsl`（価格査定・販売契約）から、構造的不変条件を検証する Alloy
モデルを `generated.als` として生成する。

## あなた（AI）が読むべき入力

1. **`input.dsl`**（このディレクトリ）— 価格査定・販売契約領域の DSL
2. **`harness/SEMANTICS.md`** — 各構文の Alloy 翻訳ルール
3. **`evaluation/case-001-inventory-lot/expected.als`** — Alloy のスタイル参照
4. **`evaluation/case-002-sales-case/expected.als`** — 販売案件 sig の参照

## スコープと境界

- case-003 の input.dsl にある宣言のみを対象
- 他ケースの sig（`LotNumber`, `LotItem`, `PreAppraisalDirectSalesCase` 等）は
  プレースホルダ sig として宣言
- DSL 内で型として宣言されない業務概念（`販売市場`, `担当者`, `加工費`,
  `期間調整率` 等の多数）も同様にプレースホルダ sig

## 生成ルール

通常の SEMANTICS.md ルールに加え、以下の特殊ケース:

### 多数のオプショナルフィールド
`ロット価格査定` は 9 個の `?` フィールドを持つ:
- 加工費?, 個別受注加算?, 等級加算?, 予約加算?, 調整率?, 品質調整率?,
  製造費単価?, 想定販売期間?, 目標利益率?
それぞれ `lone` 修飾子で宣言する

### 整数バリアント (販売方式・販売種別)
DSL は `data 販売方式 = 整数` だが、Alloy では具体値域が定義できない。
`abstract sig SalesMethod {}` のみとし、具体的な one sig は省く
（実装時に列挙化することを許容するためのプレースホルダ）

## 命名

- `通常査定` → `StandardAppraisal`
- `顧客契約査定` → `CustomerContractAppraisal`
- `予約価格` → `ReservationPrice`
- `暫定予約価格` → `TentativeReservationPrice`
- `確定予約価格` → `ConfirmedReservationPrice`
- `ロット価格査定` → `LotPricing`
- `ロット明細価格査定` → `LotItemPricing`
- `販売契約` → `SalesContract`

## 出力

このディレクトリに `generated.als` を作成。

## 検証

```bash
bash evaluation/run.sh case-003-pricing
```
