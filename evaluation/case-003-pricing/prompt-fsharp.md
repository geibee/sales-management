# Prompt: case-003 価格査定・販売契約領域 F# 生成

> **評価メソッド**: `expert-review` + `compile-check`。
> AI に渡す入力リスト・レビュー基準は `meta.md` を参照。
> リファレンス実装は別ドメイン（在庫ロット）のサンプルなので、
> **スタイル参照** に留める。

## 目的

`input.dsl`（価格査定 + 販売契約ドメイン）から、F# 型定義および
振る舞い関数を `generated.fs` として生成する。

## あなた（AI）が読むべき入力

1. **`input.dsl`**（このディレクトリ）— 価格査定・販売契約領域の DSL
2. **`harness/SEMANTICS.md`** — DSL 構文 → F# 型・関数への翻訳規則
3. **`dsl/grammar.ebnf`** — 文法（[CORE] サブセット）
4. **`harness/reference/InventoryLot.fs`** — スタイル参照のみ

## 生成する型

### 共通の前提（在庫ロット・販売案件領域から流用）
以下は他ドメインで既に定義されている前提として参照のみ:

- `LotNumber`, `LotItem`（在庫ロット領域）
- `Amount`, `NonEmptyList<'T>`（共通ユーティリティ）
- `査定前直接販売案件`, `査定済み直接販売案件`, `契約済み直接販売案件`（case-002 販売案件領域）

### 命名の指針

DSL の日本語名 → 自然な英語名:

| DSL | F# |
|---|---|
| `価格査定` | `Pricing` または `PriceAppraisal` |
| `通常査定` | `StandardAppraisal` |
| `顧客契約査定` | `CustomerContractAppraisal` |
| `査定共通` | `AppraisalCommon` |
| `ロット価格査定` | `LotPricing` |
| `ロット明細価格査定` | `LotItemPricing` |
| `予約価格` | `ReservationPrice` |
| `暫定予約価格` | `TentativeReservationPrice` |
| `確定予約価格` | `ConfirmedReservationPrice` |
| `販売契約` | `SalesContract` |
| `購入者` | `Purchaser` |
| `販売情報` | `SalesInformation` |
| `販売価格情報` | `SalesPriceInformation` |

### 既知の未定義識別子

DSL 内で参照されているが内部構造が定義されていない型:

- `査定情報`, `契約情報` — コマンド入力 DTO。レコード or branded type
- `販売市場`, `担当者`, `品目`, `納入方法`, `用途` — 業務的に branded string が妥当
- `顧客契約番号`, `顧客番号`, `代理人氏名` — branded type
- `契約調整率`, `加工費`, `個別受注加算`, `等級加算`, `予約加算`, `調整率`, `品質調整率`, `製造費単価`, `想定販売期間`, `目標利益率`, `基準単価`, `期間調整率`, `取引先調整率`, `特例期間調整率` — 数値系。`Amount` を流用するか branded type
- `予約対象ロット情報`, `予約金額`, `確定金額` — 同上
- `納期`, `査定日`, `基準単価適用日`, `期間調整率適用日`, `取引先調整率適用日`, `契約日`, `確定日` — `DateOnly`
- `税抜予定総額`, `税抜契約金額`, `消費税額`, `税抜入金額`, `入金消費税額` — `Amount`
- `支払猶予条件`, `支払猶予金額` — branded type or option
- `販売方式`, `販売種別` — DSL では `整数` だが reference の `ProcessClassification` のように DU 化を検討

## 出力

このディレクトリに `generated.fs` を作成。

## 注意点

- 命名規則は `harness/SEMANTICS.md` および reference のパターンに従う
- `data X = 整数 // 0以上` のような refined 型はスマートコンストラクタで実装
- `List<X> // 1件以上` は `NonEmptyList<X>` を使う
- 多数の `?` フィールド（特に `ロット価格査定`）は `option` で表現
- DSL に書かれていないエラーバリアントの内部構造は実装判断
- 例外は投げない。`Result<T, Error>` で返す
- 価格計算の**ロジック**は対象外（`docs/decision-tables/pricing-rules.md` で扱う予定）

## 検証

生成後、リポジトリルートから:

```bash
bash evaluation/run.sh case-003-pricing
```

- `compile-check` が自動で実行される（dotnet があれば）
- `expert-review` のチェックリストは `meta.md` を参照して人間が判定
