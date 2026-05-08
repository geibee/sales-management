# evaluation/

DSL → 各派生物（F# 型定義・Mermaid 図など）の **AI 生成パイプライン** を
継続検証するためのケース集。

> 設計原則（HANDOFF）: トランスパイラを書かない。文法 + 意味論ガイド +
> リファレンス実装の3点を整え、生成は毎回AIに任せる。

---

## リファレンス実装の位置付け（重要）

`harness/reference/InventoryLot.fs` は **在庫ロット領域の AI 翻訳スタイル
サンプル**にすぎない。以下を**前提にしてはならない**:

- ❌ 各ドメイン（販売案件・価格査定・販売契約など）に対応するリファレンスが存在する
- ❌ AI 生成物はリファレンスと完全一致しなければならない
- ❌ `expected.<ext>` は全ケースに必須

リファレンスは「**こういうスタイルで翻訳してほしい**」という見本であり、
各ケースのゴールド標準ではない。

---

## 何を評価するのか

評価対象は重なっており、ケースごとに**主目的**が異なる。

| 評価対象 | 何を確認したいか | 検出したい兆候 |
|---|---|---|
| **A. パイプラインの健全性** | AI 生成という仕組みが機能しているか | 出力が空・破綻・タイムアウト |
| **B. 翻訳規則の十分性** | `SEMANTICS.md` だけで AI が一貫した出力を出せるか | 出力が荒れる／一貫しない |
| **C. DSL の表現力** | DSL に必要な情報が揃っているか | AI が「情報不足」と判断する箇所 |
| **D. 回帰検出** | DSL / SEMANTICS / reference の変更で出力が壊れていないか | 既存ケースで diff が肥大化 |

---

## 評価メソッド（ケースごとに 1 つ以上選ぶ）

| メソッド | 必要な成果物 | 自動化 | 主にどの評価対象に効くか |
|---|---|---|---|
| **`sample-diff`** | curated な `expected.<ext>` | ✅ 意味論比較 (normalize.py) | A, D |
| **`compile-check`** | 言語ツールチェイン (例: `dotnet build`) | ✅ コンパイラ実行 | A |
| **`smoke-check`** | 出力に含まれるべき識別子のリスト | ✅ 文字列マッチ | A |
| **`expert-review`** | レビュー基準・チェックリスト | ❌ 人手 | B, C |
| **`consistency-check`** | 同入力で N 回生成 → ばらつき測定 | ✅ 多重実行 + 距離計算 | B |

`sample-diff` は **サンプルが用意できるケースだけ**で使う。多くのケースでは
`compile-check` + `expert-review` の組み合わせになる。

---

## ディレクトリ構成

```
evaluation/
├── README.md                       # このファイル
├── run.sh                          # 評価ランナー
└── case-NNN-<name>/
    ├── meta.md                     # 評価目的・メソッド・AI に渡す入力・合格基準
    ├── input.dsl                   # DSL（domain-model.md からの抽出）
    ├── expected.<ext>              # （任意）サンプル比較用ゴールド出力
    ├── prompt-<target>.md          # ターゲット別 AI 指示書
    └── generated.<ext>             # AI が生成した結果（gitignore 対象）
```

ターゲットは生成ファイルの拡張子で識別:

| 拡張子 | ターゲット |
|---|---|
| `.fs` | F# 型定義・関数 |
| `.mmd` | Mermaid 状態遷移図 |
| `.als` | Alloy モデル（P3 で導入予定） |
| `.tla` | TLA+ モデル（P3 で導入予定） |

---

## ループ

1. 該当ケースの `meta.md` を読み、評価メソッドに沿った入力を AI に渡す
2. AI が `generated.<ext>` を生成
3. `bash evaluation/run.sh case-NNN-<name>` を実行
4. メソッドごとの判定:
   - `sample-diff` → expected との **意味論比較**（コメント・空行・末尾空白を除去して比較。`normalize.py` 経由）
   - `compile-check` → コンパイラ exit code
   - `smoke-check` → 必須識別子の有無
   - `expert-review` → ランナーは [PENDING REVIEW] を出すのみ
   - `consistency-check` → 複数 generated を比較

### sample-diff の判定値

| 判定 | 意味 |
|---|---|
| `[OK] 完全一致` | バイト単位で一致 |
| `[OK] 意味論的に等価（コメント・空行差のみ）` | normalize 後に一致 |
| `[DIFF] +N -M 行（意味論的差分）` | normalize 後も差分あり。要対処 |

---

## 既存ケース

| ケース | 領域 | 主メソッド | 補助メソッド |
|---|---|---|---|
| `case-001-inventory-lot` | 在庫ロット | `sample-diff`（reference を流用） | `compile-check` |
| `case-002-sales-case` | 販売案件（未着手） | `expert-review` | `compile-check` |
| `case-003-pricing` | 価格査定（未着手） | `expert-review` | `compile-check` |

---

## generated.\<ext\> を gitignore する理由

毎回 AI が生成し直す前提のため、コミットしない。
`expected.<ext>`（サンプルがある場合のみ）は SSoT として残す。
