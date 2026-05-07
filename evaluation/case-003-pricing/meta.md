# case-003 価格査定・販売契約 — メタ情報

## 領域

価格査定（通常査定・顧客契約査定）と販売契約。在庫ロット領域のロット番号・
ロット明細を参照する。

## 評価メソッド

| メソッド | 内容 |
|---|---|
| `expert-review` | 主メソッド。生成 F# のレビュー基準で人手判定 |
| `compile-check` | `generated.fs` が F# として通るか確認（dotnet があれば自動） |

このケースには **`sample-diff` を使わない**（リファレンス実装が無いため）。

## AI に渡す入力

- `dsl/grammar.ebnf`（[CORE] サブセット）
- `harness/SEMANTICS.md`（[CORE] / [CORE 派生ビュー] 章）
- `harness/reference/InventoryLot.fs`（**スタイルサンプル**として）
- `evaluation/case-003-pricing/input.dsl`
- `evaluation/case-003-pricing/prompt-<target>.md`

## レビュー基準（expert-review チェックリスト）

### 構造
- [ ] `価格査定 = 通常査定 OR 顧客契約査定` が判別共用体に翻訳されている
- [ ] `予約価格 = 暫定予約価格 OR 確定予約価格` が判別共用体に翻訳されている
- [ ] `査定共通` が `通常査定` と `顧客契約査定` の共通フィールドとして含まれている
- [ ] `予約価格共通` が `暫定予約価格` と `確定予約価格` の共通フィールドとして含まれている
- [ ] `List<ロット価格査定>`, `List<ロット明細価格査定>` が `NonEmptyList` で表現されている
- [ ] オプショナル (`?`) が `option` に翻訳されている（特に `ロット価格査定` の多数の任意フィールド）

### 命名（自然な英訳）
- [ ] `価格査定` → `Pricing` または `PriceAppraisal`
- [ ] `通常査定` → `StandardAppraisal`
- [ ] `顧客契約査定` → `CustomerContractAppraisal`
- [ ] `予約価格` → `ReservationPrice`
- [ ] `暫定予約価格` → `TentativeReservationPrice`
- [ ] `確定予約価格` → `ConfirmedReservationPrice`
- [ ] `販売契約` → `SalesContract`
- [ ] `購入者` → `Purchaser`

### 振る舞い
- [ ] 5 個の `behavior` が関数として実装されている（価格査定 3 + 販売契約 2）
- [ ] 入力型 / 出力型 / エラー型のシグネチャが DSL と一致
- [ ] 入力に `査定済み直接販売案件` 等の販売案件型を取る場合、それは case-002 で定義された型を `open` で参照する想定

### 設計原則
- [ ] Make Illegal States Unrepresentable（査定が無い `契約済み直接販売案件` は型として作れない、など）
- [ ] 純粋関数（副作用なし）
- [ ] 例外を投げていない

## このケースで検出したいこと（評価対象 B, C）

- 多数のオプショナルフィールドを持つ `ロット価格査定` を AI が `option` の連鎖として正しく扱えるか
- DSL に内部構造の記載がない型（`査定情報`, `契約情報`, `販売市場`, `担当者`, `品目`, `納入方法`, `用途` 等）を AI がどう扱うか（プレースホルダ型 vs 文字列）
- `販売種別 = 整数` のような曖昧な定義を、reference の `ProcessClassification` 等のように DU に厳密化するか、あるいは branded type のままにするか

## 既知の課題

- `査定情報`, `契約情報` などはコマンド DTO 的なオブジェクトで、内部構造が DSL に書かれていない
- `販売市場`, `担当者`, `品目`, `納入方法`, `用途` 等が型として宣言されておらず、文字列扱いか branded type かは AI 判断
- `予約対象ロット情報` も DSL に内部構造の記載なし
- 価格計算の **ロジック** は P5 の決定表で扱う（このケースでは型定義のみ）
