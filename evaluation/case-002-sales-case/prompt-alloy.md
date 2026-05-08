# Prompt: case-002 販売案件 Alloy 生成

> **評価メソッド**: `sample-diff`。
> AI に渡す入力リスト・判定基準は `meta.md` を参照。

## 目的

`input.dsl`（販売案件ドメイン）から、構造的不変条件を検証する Alloy
モデルを `generated.als` として生成する。

## あなた（AI）が読むべき入力

1. **`input.dsl`**（このディレクトリ）— 販売案件領域の DSL
2. **`harness/SEMANTICS.md`** — 各構文の Alloy 翻訳ルール
3. **`evaluation/case-001-inventory-lot/expected.als`** — Alloy のスタイル参照

## スコープと境界

- **case-002 の input.dsl にある宣言のみ**を対象とする
- 他ケースで定義される sig（`InventoryLot` / `ManufacturedLot` / `Pricing` /
  `SalesContract` / `ReservationPrice` / `DateOnly` / `DivisionCode`）は
  プレースホルダ sig として宣言する（実際のフィールドは省く）
- DSL に内部構造の記載がない型（`委託業者情報`, `委託販売結果`, `予約価格情報`,
  `販売案件種別`）も同様にプレースホルダ sig

## 生成ルール（SEMANTICS.md より要約）

| DSL | Alloy |
|---|---|
| `data X = A AND B` | `sig X { a: one A, b: one B }` |
| `data X = A OR B OR C` | `abstract sig X {}` + `sig A extends X {...}` |
| `AND F?` | `f: lone F` |
| `AND List<F>` // 1件以上 | `f: some F` |
| `behavior B = I -> O` | `pred B[i: I, o: O] { 事後条件 }` |

## 命名

- `直接販売案件` → `DirectSalesCase`
- `予約販売案件` → `ReservationSalesCase`
- `委託販売案件` → `ConsignmentSalesCase`
- 各状態 `〜済み` / `〜指示済み` 等の語幹が一貫していること
- 最上位の `販売案件 = 直接販売案件 OR 予約販売案件 OR 委託販売案件` は
  3 つのサブドメインがすでに `abstract sig` を持つため、
  ラッパー sig（`DirectSalesCaseRef` など）で `extends SalesCase` する

## 出力

このディレクトリに `generated.als` を作成。

## 注意点

- `module SalesCase` を冒頭に置く
- DSL に書かれていない型はプレースホルダの空 sig として宣言
- 各 behavior に対応する `pred` を作る（事後条件として共通フィールドの保存を記述）
- 末尾に主要な `pred` の `run` コマンドを追加（`for 4` 程度）

## 検証

```bash
bash evaluation/run.sh case-002-sales-case
```
