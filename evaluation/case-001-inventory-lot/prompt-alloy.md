# Prompt: case-001 在庫ロット領域 Alloy 生成

> **評価メソッド**: `sample-diff`。
> AI に渡す入力リスト・判定基準は `meta.md` を参照。
> リファレンス／expected は**翻訳スタイルのサンプル**であって厳格な正解ではない。

## 目的

`input.dsl`（在庫ロット領域）から、構造的不変条件を検証する Alloy モデルを
`generated.als` として生成する。

## あなた（AI）が読むべき入力

1. **`input.dsl`**（このディレクトリ）— 在庫ロット領域の DSL
2. **`harness/SEMANTICS.md`** — 各構文の Alloy 翻訳ルール（§1, 3, 4, 5, 6, 7）
3. **`dsl/grammar.ebnf`** — 文法（[CORE] サブセット）

## 生成ルール（SEMANTICS.md より要約）

| DSL 構文 | Alloy |
|---|---|
| `data X = 整数 // 0以上` | `sig X { value: Int }` + `fact { all x: X \| x.value >= 0 }` |
| `data X = 整数`（単純） | `sig X { value: Int }` |
| `data X = 数値`（decimal） | 抽象 `sig X {}`（Alloy の Int は精度不足） |
| `data X = A AND B AND C` | `sig X { a: one A, b: one B, c: one C }` |
| `data X = A OR B OR C` | `abstract sig X {}` + `sig A extends X {}` 等 |
| `AND F?` | `f: lone F` |
| `AND List<F>` // 1件以上 | `f: some F` |
| `behavior B = I -> O` | `pred B[i: I, o: O] { 事後条件 }` |

## スコープ

- **[CORE] のみ**（型構造と振る舞いシグネチャ）
- 値域制約（`Amount` >= 0、`Count` >= 1）はコメント由来の慣例として
  最小限のみ `fact` に含める
- 横断的不変条件 (`invariant`)、時相性質 (`property`)、初期状態 (`initial`)
  は **VERIFICATION 層**なのでこのケースでは生成しない（P4）

## 命名

- 識別子は DSL の日本語名から自然な英訳（reference の F# と同じ規約）
- 区分のバリアント名で他の sig と衝突する場合はサフィックス付与
  - 例: `ManufacturingClassification.Standard` と `ItemClassification.Standard`
    が衝突する → `StandardManufacturing` / `StandardItem` のように区別
  - 例: `品目区分 = 一般品 OR 上位品 OR 特注品` → `StandardItem` / `PremiumItem` / `CustomMadeItem`

## 出力

このディレクトリに `generated.als` を作成。

## 注意点

- `module InventoryLot` 宣言を冒頭に置く
- DSL に内部構造の記載が無い型（`変換先情報`, `日付` 等）は抽象 sig として宣言
- 日付型は `sig DateOnly {}` のような抽象として表現
- 各 behavior に対して動作確認用の `run` コマンドを末尾に追加（`for 4` 程度）

## 検証

生成後、リポジトリルートから:

```bash
bash evaluation/run.sh case-001-inventory-lot
```

`expected.als` との diff が出力される。差分が「意味論的に等価」かを人間が判定する。
