# Prompt: case-002 販売案件 Mermaid 状態遷移図 生成

> **評価メソッド**: `sample-diff`。
> AI に渡す入力リスト・判定基準は `meta.md` を参照。

## 目的

`input.dsl`（販売案件ドメイン）から、状態遷移を表現する Mermaid
`stateDiagram-v2` を `generated.mmd` として生成する。

## あなた（AI）が読むべき入力

1. **`input.dsl`**（このディレクトリ）— 販売案件領域の DSL
2. **`harness/SEMANTICS.md` §12**（Mermaid 翻訳規則）

## 生成ルール（SEMANTICS.md §12 より）

- DSL の **直和型 (`OR`)** のバリアントを状態として記述
- DSL の **behavior** を状態遷移として記述（Error バリアントは図に含めない）
- 入出力が直和型のバリアントのときに遷移を描く
- 中間データ (`AND` で結合された付随情報) を持つ behavior は遷移として描かない
  例: `販売案件を作成する = List<製造完了ロット> AND 販売案件種別 -> 販売案件 ...`
       これは入力が直和型のバリアントでないため `[*] --> 各サブ状態` として描く

## スコープ

- **case-002 の input.dsl にある behavior のみ**を描画する
- 査定 (`価格査定を作成する` 等) や契約 (`販売契約を締結する` 等) の遷移は
  case-003 の input.dsl に含まれる behavior のため、この図には**現れない**
- このため `直接販売案件` の `査定前 → 査定済み → 契約済み` の遷移は
  case-002 では描かれず、`契約済み → 出荷指示済み → 出荷完了` 部分だけになる

## 名前

ノードラベルは DSL の日本語識別子をそのまま使う。

## 出力

このディレクトリに `generated.mmd` を作成。

## 検証

```bash
bash evaluation/run.sh case-002-sales-case
```
