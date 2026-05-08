# Prompt: case-001 在庫ロット領域 Mermaid 状態遷移図 生成

> **評価メソッド**: `sample-diff`。
> AI に渡す入力リスト・判定基準は `meta.md` を参照。
> リファレンス／expected は**翻訳スタイルのサンプル**であって厳格な正解ではない。

## 目的

`input.dsl` の DSL から、`expected.mmd` と意味論的に一致する
Mermaid `stateDiagram-v2` を `generated.mmd` として生成する。

## あなた（AI）が読むべき入力

1. **`input.dsl`**（このディレクトリ）— 在庫ロット領域の DSL
2. **`harness/SEMANTICS.md`** — 特に Mermaid 翻訳節（Mermaid 章）
3. **`dsl/grammar.ebnf`** — 文法 [CORE] サブセット

## 生成ルール

- DSL 中の **直和型 (`OR`)** を「状態の場合分け」と解釈し、
  各バリアントを Mermaid の状態として記述
- DSL 中の **behavior** を状態遷移として記述:
  - `behavior X = StateA AND ... -> StateB OR Error` →
    `StateA --> StateB : X` の遷移を追加
  - StateA / StateB が直和型のバリアントのときに遷移を描く
  - Error バリアントは図に含めない（業務上の正常遷移のみ）
- 初期状態（最初の状態）に `[*] --> InitialState` を加える
- 終端状態（出口）には `FinalState --> [*]` を加える

## 出力

このディレクトリに `generated.mmd` を作成。

## 検証

```bash
bash evaluation/run.sh case-001-inventory-lot
```

`expected.mmd` との diff が出力される。差分が「意味論的に等価」かを人間が判定する。
