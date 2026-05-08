# Prompt: case-001 在庫ロット領域 TLA+ 生成

> **評価メソッド**: `sample-diff`。
> AI に渡す入力リスト・判定基準は `meta.md` を参照。
> リファレンス／expected は**翻訳スタイルのサンプル**であって厳格な正解ではない。

## 目的

`input.dsl`（在庫ロット領域）から、状態と振る舞いを表現する TLA+ 仕様を
`generated.tla` として生成する。

## あなた（AI）が読むべき入力

1. **`input.dsl`**（このディレクトリ）— 在庫ロット領域の DSL
2. **`harness/SEMANTICS.md`** — 各構文の TLA+ 翻訳ルール（§1, 3, 4, 5, 6, 7）
3. **`dsl/grammar.ebnf`** — 文法（[CORE] サブセット）

## 生成ルール

### 全体構造

```
---- MODULE InventoryLot ----
EXTENDS ...
CONSTANTS ...      (* 値の domain *)
(* type definitions *)
VARIABLES inventoryLots
TypeOK == ...
Init == ...
(* actions *)
Next == \E lot \in inventoryLots : \/ Action1(lot, ...) \/ ...
Spec == Init /\ [][Next]_vars
====
```

### DSL → TLA+ 対応表

| DSL | TLA+ |
|---|---|
| `data X = 整数 // 0以上` | `X == { x \in Nat : x >= 0 }` |
| `data X = 整数`（単純） | `X == Nat` または CONSTANTS で domain として宣言 |
| `data X = 数値`（decimal） | `X == Nat`（精度抽象化） |
| `data X = A AND B AND C` | `X == [a: A, b: B, c: C]` |
| `data X = A OR B OR C` | バリアントごとに `[state: {"A"}, ...]` を定義し `\cup` で和集合 |
| `AND F?` | `f: F \cup {NULL}` |
| `AND List<F>` // 1件以上 | `f: { s \in SUBSET F : Cardinality(s) >= 1 }` |
| `behavior B = I -> O` | アクション述語: 事前条件で入力状態を絞り、`inventoryLots'` で次状態を構築 |

### 状態モデル

- 単一の状態変数 `inventoryLots`（在庫ロットの集合）を持つ
- 各 behavior は `inventoryLots' = (inventoryLots \ {lot}) \cup {新ロット}` の形で
  ロットを置き換える
- `state` フィールドのタグで sum type のバリアントを識別

## スコープ

- **[CORE] のみ**（型構造と振る舞いシグネチャ）
- 値域制約（`Amount` >= 0、`Count` >= 1）はコメント由来の慣例として
  集合内包記法で最小限のみ含める
- 横断的不変条件 (`invariant`)、liveness (`property`)、初期状態の値
  指定は **VERIFICATION 層**なのでこのケースでは生成しない（P4）
- ただし基本的な型不変条件 `TypeOK == inventoryLots \subseteq InventoryLot` は含める

## 命名

- 識別子は DSL の日本語名から自然な英訳（reference の F# と同じ規約）
- 区分のバリアントで他と衝突する場合はサフィックス付与
  - 例: `品目区分 = 一般品 OR 上位品 OR 特注品` → `"StandardItem"` / `"PremiumItem"` / `"CustomMadeItem"`
  - 例: `製造区分` のバリアントは `"StandardManufacturing"` / `"CustomManufacturing"`

## 出力

このディレクトリに `generated.tla` を作成。

## 注意点

- ファイル冒頭に `---- MODULE InventoryLot ----` を置く
- `EXTENDS Naturals, FiniteSets` 程度を使う
- DSL に内部構造の記載が無い型（`日付`, `事業部コード` など）は CONSTANTS で抽象的に宣言
- ファイル末尾に `====` を置く

## 検証

生成後、リポジトリルートから:

```bash
bash evaluation/run.sh case-001-inventory-lot
```

`expected.tla` との diff が出力される。差分が「意味論的に等価」かを人間が判定する。
