# evaluation/

DSL → 各派生物（F# 型定義など）の生成を **AI に毎回任せる** ことを前提に、
生成結果がリファレンス実装と意味論的に一致するかを評価するためのケース集。

> 設計原則（HANDOFF）: トランスパイラを書かない。文法 + 意味論ガイド +
> リファレンス実装の3点を整え、生成は毎回AIに任せる。

## ディレクトリ構成

```
evaluation/
├── README.md                       # このファイル
├── run.sh                          # 評価ランナー
└── case-NNN-<name>/
    ├── input.dsl                   # DSL（domain-model.md からの抽出、共通入力）
    ├── expected.<ext>              # ターゲット別ゴールド標準（fs / mmd / als / tla）
    ├── prompt-<target>.md          # ターゲット別 AI 指示書
    └── generated.<ext>             # AI が生成した結果（gitignore 対象）
```

ターゲットは `expected.<ext>` の拡張子で識別:

| 拡張子 | ターゲット |
|---|---|
| `.fs` | F# 型定義・関数 |
| `.mmd` | Mermaid 状態遷移図 |
| `.als` | Alloy モデル（P3 で導入予定） |
| `.tla` | TLA+ モデル（P3 で導入予定） |

## ループ

1. `prompt.md` を AI（Claude Code 等）に渡し、`input.dsl` から
   `generated.fs` を生成させる
2. `bash evaluation/run.sh case-NNN-<name>` を実行
3. `expected.fs` との diff を人間が判定:
   - **完全一致** → ✅
   - **意味論的に等価** な差分（コメント・順序のみ） → ✅、`expected.fs` を更新するか判断
   - **構造的差分** → AI への指示（prompt.md / SEMANTICS.md）を改善

## 評価ケース

| ケース | 領域 | 状態 |
|---|---|---|
| `case-001-inventory-lot` | 在庫ロット | リファレンス実装あり |
| `case-002-sales-case` | 販売案件 | 未着手 |
| `case-003-pricing` | 価格査定 | 未着手 |

## generated.fs を gitignore する理由

毎回 AI が生成し直す前提のため、コミットしない。
`expected.fs`（リファレンス由来）のみが SSoT。
