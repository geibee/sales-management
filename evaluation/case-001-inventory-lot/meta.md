# case-001 在庫ロット — メタ情報

## 評価目的タグ

**`regression`**

リファレンス実装 `harness/reference/InventoryLot.fs` を AI が再現できるかを
確認する。`SEMANTICS.md` の翻訳規則・命名規約・スタイルが破綻していない
ことのサニティチェック。

## AI に渡す入力

このケースは regression テストなので、**リファレンスを含めて**渡す:

- `dsl/grammar.ebnf`（[CORE] サブセット）
- `harness/SEMANTICS.md`（[CORE] / [CORE 派生ビュー] 章）
- `harness/reference/InventoryLot.fs` ← **見せる**
- `evaluation/case-001-inventory-lot/input.dsl`
- `evaluation/case-001-inventory-lot/prompt-<target>.md`

## 合格基準

| ターゲット | 基準 |
|---|---|
| F# (`generated.fs`) | `expected.fs` と意味論的に等価。型構造・型名・フィールド名・関数シグネチャは完全一致を要求。順序・コメント・docstring 文言の差は許容 |
| Mermaid (`generated.mmd`) | `expected.mmd` と意味論的に等価。状態名・遷移ラベル・初期/終端状態の整合が取れていること |

## このケースで検出したいこと

- 命名規約（`ManufacturedLot` / `ShippingInstructedLot` / `ManufacturingCompletedDate` など）の遵守
- smart constructor パターンの適用（DSL コメントの `// 0以上` などを検出）
- 直和型 → 判別共用体への 1:1 マッピング
- behavior の Result 戻り型・エラー型構造化
- Mermaid の状態遷移が DSL behavior と一貫すること

## 弱点

リファレンスを見せて再現させる構造のため、以下は検出しにくい:

- `SEMANTICS.md` の不足や曖昧さ
- AI が rule を一般化して未知領域に適用する能力

これらは `case-002`（販売案件）以降の **generalization** ケースで補完する。
