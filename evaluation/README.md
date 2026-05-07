# evaluation/

DSL → 各派生物（F# 型定義・Mermaid 図など）の **AI 生成パイプライン** を
継続検証するためのケース集。

> 設計原則（HANDOFF）: トランスパイラを書かない。文法 + 意味論ガイド +
> リファレンス実装の3点を整え、生成は毎回AIに任せる。

---

## 何を評価するのか

評価対象は重なっており、ケースごとに**主目的**が異なる。

| 評価対象 | 何を確認したいか | 検出したい兆候 |
|---|---|---|
| **A. パイプラインの健全性** | AI 生成という仕組みが機能しているか | 出力が空・破綻・タイムアウト |
| **B. 翻訳規則の十分性** | `SEMANTICS.md` だけで AI が一貫した出力を出せるか | リファレンスを見せないと出力が荒れる |
| **C. DSL の表現力** | DSL に必要な情報が揃っているか | AI が「情報不足」と判断する箇所 |
| **D. 回帰検出** | DSL / SEMANTICS / reference の変更で出力が壊れていないか | 既存ケースで diff が肥大化 |

---

## ケース分類

ケース名・配置とは別に、各ケースは **評価目的タグ**を持つ:

| タグ | 説明 | AI に渡す入力 |
|---|---|---|
| **regression** | リファレンス実装の再現性を見る。最小限のサニティチェック | `SEMANTICS.md` + `grammar.ebnf` + **`reference/`** + `input.dsl` |
| **generalization** | リファレンス**なし**で SEMANTICS だけで生成できるか見る。翻訳規則の十分性を測る | `SEMANTICS.md` + `grammar.ebnf` + `input.dsl` |
| **consistency** | 同じ入力で複数回生成して**ばらつき**を見る | regression / generalization と同じ入力で N 回実行 |

タグは各ケースの `meta.md` に記載。

---

## ディレクトリ構成

```
evaluation/
├── README.md                       # このファイル
├── run.sh                          # 評価ランナー
└── case-NNN-<name>/
    ├── meta.md                     # 評価目的・AI に渡す入力・合格基準
    ├── input.dsl                   # DSL（domain-model.md からの抽出）
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

---

## ループ

1. 該当ケースの `meta.md` を読み、評価目的に沿った入力を AI に渡す
2. AI が `generated.<ext>` を生成
3. `bash evaluation/run.sh case-NNN-<name>` を実行
4. `expected.<ext>` との diff を判定:
   - **完全一致** → ✅
   - **意味論的に等価**（コメント・順序差） → ✅、必要なら expected を更新
   - **構造的差分** → 評価目的に応じて対処:
     - regression なら AI 不調 or reference 更新漏れ
     - generalization なら SEMANTICS.md / prompt の改善が必要

---

## 既存ケース

| ケース | 領域 | 主目的 | リファレンス |
|---|---|---|---|
| `case-001-inventory-lot` | 在庫ロット | regression | あり (`harness/reference/InventoryLot.fs`) |
| `case-002-sales-case` | 販売案件 | generalization（未着手） | なし |
| `case-003-pricing` | 価格査定 | generalization（未着手） | なし |

---

## generated.\<ext\> を gitignore する理由

毎回 AI が生成し直す前提のため、コミットしない。
`expected.<ext>`（リファレンス由来 or 人手で作成したゴールド標準）のみが SSoT。
