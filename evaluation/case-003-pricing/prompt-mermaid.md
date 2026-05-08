# Prompt: case-003 価格査定・販売契約 Mermaid 状態遷移図 生成

> **評価メソッド**: `sample-diff`。
> AI に渡す入力リスト・判定基準は `meta.md` を参照。

## 目的

`input.dsl`（価格査定・販売契約）の behavior が引き起こす **直接販売案件の
査定・契約状態遷移**を `stateDiagram-v2` で `generated.mmd` として生成する。

## あなた（AI）が読むべき入力

1. **`input.dsl`**（このディレクトリ）— 価格査定・販売契約領域の DSL
2. **`harness/SEMANTICS.md` §12**（Mermaid 翻訳規則）

## スコープ

- case-003 の input.dsl にある behavior のみを描画
  - 5 個の behavior: 価格査定を{作成/更新/削除}する、販売契約を{締結/削除}する
- これらは `直接販売案件` のサブ状態間（査定前 ↔ 査定済み ↔ 契約済み）の
  遷移を引き起こす
- 出庫・予約・委託系の遷移は case-002 の Mermaid に含まれる

## 生成ルール

- `behavior X = StateA AND _ -> StateB OR Error` → `StateA --> StateB : X`
- 自己ループ（`StateA --> StateA`）も描く（例: 価格査定を更新する）
- Error バリアントは図に含めない

## 名前

ノードラベルは DSL の日本語識別子（`査定前直接販売案件` 等）をそのまま使う。

## 出力

このディレクトリに `generated.mmd` を作成。

## 検証

```bash
bash evaluation/run.sh case-003-pricing
```
