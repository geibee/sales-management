# Prompt: case-002 販売案件領域 F# 生成

> **評価メソッド**: `expert-review` + `compile-check`。
> AI に渡す入力リスト・レビュー基準は `meta.md` を参照。
> リファレンス実装は別ドメイン（在庫ロット）のサンプルなので、
> **スタイル参照** に留める。

## 目的

`input.dsl`（販売案件ドメイン）から、F# 型定義および振る舞い関数を
`generated.fs` として生成する。

## あなた（AI）が読むべき入力

1. **`input.dsl`**（このディレクトリ）— 販売案件領域の DSL
2. **`harness/SEMANTICS.md`** — DSL 構文 → F# 型・関数への翻訳規則。
   特に `[CORE]` ラベルの章（§1, 3, 4, 5, 6, 7）が今回のスコープ。
3. **`dsl/grammar.ebnf`** — 文法（[CORE] サブセット）
4. **`harness/reference/InventoryLot.fs`** — F# のスタイル参照。
   命名規則・smart constructor パターン・エラー型構造はこれに従う。
   ただし**在庫ロット領域専用**であり、内容を流用しない。

## 生成する型

### 共通の前提（在庫ロット領域から流用）
以下は他ドメインで既に定義されている前提として参照のみ:

- `InventoryLot`, `ManufacturedLot`, `ManufacturingLot`（在庫ロット領域）
- `DivisionCode`, `Amount`, `Count`, `Quantity`（基本値型）
- `NonEmptyList<'T>`（共通ユーティリティ）

このファイルでは**販売案件側の宣言のみ**を生成する。在庫ロット側の型は
`open` で参照する想定。

### 命名の指針

DSL の日本語名 → 自然な英語名:

| DSL | F# |
|---|---|
| `販売案件` | `SalesCase` |
| `直接販売案件` | `DirectSalesCase` |
| `予約販売案件` | `ReservationSalesCase` |
| `委託販売案件` | `ConsignmentSalesCase` |
| `査定前直接販売案件` | `PreAppraisalDirectSalesCase`（または同等） |
| `契約済み直接販売案件` | `ContractedDirectSalesCase` |
| `仮予約案件` | `TentativeReservationCase` |
| `予約確定済み案件` | `ConfirmedReservationCase` |
| `予約納品済み案件` | `DeliveredReservationCase` |
| `委託指定済み販売案件` | `ConsignmentAssignedCase` |
| `委託販売結果入力済み販売案件` | `ConsignmentResultEnteredCase` |

不明な訳語は、業務上の意味を保つ自然な英訳を選ぶこと。

### 既知の未定義識別子

DSL 内で参照されているが他領域で定義される（または未定義の）型:

- `価格査定`, `販売契約`, `予約価格` — 価格査定領域（case-003 で扱う）
- `出荷指示情報` — このファイル内で定義（`出荷指示日 = DateOnly`）
- `委託業者情報`, `委託販売結果`, `予約価格情報`, `販売案件種別` — DSL に内部構造の記載なし。プレースホルダ型として branded type で宣言する
- `製造完了ロット`, `在庫ロット` — 在庫ロット領域から `open` で参照

## 出力

このディレクトリに `generated.fs` を作成。

## 注意点

- 命名規則は `harness/SEMANTICS.md` および `harness/reference/InventoryLot.fs` のパターンに従う
- `data X = 整数 // 0以上` のような refined 型はスマートコンストラクタで実装
- `List<X> // 1件以上` は `NonEmptyList<X>` を使う
- DSL に書かれていないエラーバリアントの内部構造は実装判断
- 例外は投げない。`Result<T, Error>` で返す
- 状態を直和型で表現し、不正な遷移が型レベルで作れないようにする

## 検証

生成後、リポジトリルートから:

```bash
bash evaluation/run.sh case-002-sales-case
```

- `compile-check` が自動で実行される（dotnet があれば）
- `expert-review` のチェックリストは `meta.md` を参照して人間が判定
