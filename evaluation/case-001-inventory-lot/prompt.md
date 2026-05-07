# Prompt: case-001 在庫ロット領域 F# 生成

## 目的

`input.dsl` の DSL から、`expected.fs` と意味論的に一致する F# 型定義および
振る舞い関数を `generated.fs` として生成する。

## あなた（AI）が読むべき入力

1. **`input.dsl`**（このディレクトリ）— 在庫ロット領域の DSL
2. **`harness/SEMANTICS.md`** — DSL 構文 → F# 型・関数への翻訳規則。
   特に `[CORE]` ラベルの章（§1, 3, 4, 5, 6, 7）が今回のスコープ。
3. **`dsl/grammar.ebnf`** — 文法（[CORE] サブセット）
4. **`harness/reference/InventoryLot.fs`** — F# のスタイル基準。
   命名規則・smart constructor パターン・エラー型構造はこれに従う。

## 出力

このディレクトリに `generated.fs` を作成。`expected.fs` と意味論的に
（フィールド名・型名・関数シグネチャレベルで）一致すること。

## 注意点

- 命名規則は `harness/SEMANTICS.md` および `harness/reference/InventoryLot.fs` に従う
  - `製造完了ロット` → `ManufacturedLot`（`CompletedLot` ではない）
  - `出荷指示済みロット` → `ShippingInstructedLot`（`ShipmentInstructedLot` ではない）
  - `製造完了日` → `ManufacturingCompletedDate`
- `data X = 整数 // 0以上` のようなコメントは smart constructor の
  値域制約として実装する（[VERIFICATION] 拡張だが慣例として行う）
- `List<X> // 1件以上` は `NonEmptyList<X>` を使う
- DSL に書かれていないエラーバリアントの内部構造は実装判断
  （文字列メッセージや日付などを含めて構造化する）
- 例外は投げない。`Result<T, Error>` で返す

## 検証

生成後、リポジトリルートから:

```bash
bash evaluation/run.sh case-001-inventory-lot
```

`expected.fs` との diff が出力される。差分が「意味論的に等価」かを人間が判定する。
