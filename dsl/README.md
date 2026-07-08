# Stage 1 仕様入力

このディレクトリは、仕様駆動開発の Stage 1 入力を置く場所。

Stage 1 の目的は、ドメインモデルを **型** と **behavior の型シグネチャ** として整理し、AI に F# 実装へ反映させ、F# コンパイラとテストで矛盾を検出すること。

## 方針

- 専用パーサーは使わない
- IR生成はしない
- Alloy / TLA+ / PBT 用の仕様はまだ管理しない
- `domain-model.md` を人間とAIが読む仕様入力にする
- 最終的な検査は `dotnet build` と `dotnet test` で行う

## 書くもの

`domain-model.md` には、関数型ドメインモデリングの観点で次だけを書く。

```text
data X = A
data X = A AND B
data X = A OR B
data X = A?
data X = List<A> // 1件以上

behavior F = Input -> Output OR Error
```

特に、状態によって必須項目が変わるものは `status` フィールドではなく OR 型で分ける。

```text
data 直接販売案件 =
  査定前直接販売案件
  OR 査定済み直接販売案件
  OR 契約済み直接販売案件
```

## 書かないもの

Stage 1 では次を仕様入力にしない。

- 独自DSLの厳密な文法
- パーサー用の都合
- IRスキーマ
- Alloy / TLA+ のモデル
- PBT のテストオラクル
- DB制約、API都合、画面都合
- モナドや `Task<Result<_,_>>` などの実装上の効果表現

型で表現できない仕様は、まず F# の純粋関数・テスト・コメントで扱う。必要性が明確になってから、次の段階として専用の検証仕様を検討する。

## behavior と実装の整合ゲート

`behavior` 行には行末に `// fn: <Module>.<関数名>` の注釈を付ける。
verify の DSL 整合ゲート (`apps/api-fsharp/scripts/dsl-consistency.py`) が
「全 behavior に注釈があること」と「注釈先の関数が `src/SalesManagement/` の
該当モジュールに実在すること」を突合する。grep レベルの決定的検査であり、
専用パーサーは引き続き導入しない (issue #9 §3)。behavior を追加したのに
実装 (と注釈) が無いと verify が赤になる。

## 作業手順

1. `domain-model.md` の型と `behavior` を更新する (実装したら `// fn:` 注釈を付ける)
2. AI に `domain-model.md` と既存の `apps/api-fsharp/src/SalesManagement/Domain/` を読ませる
3. F# の型定義、エラー型、workflow の純粋関数を更新する
4. `dotnet build` を通す
5. 影響がある workflow は `dotnet test` で確認する

Stage 1 では、仕様文書そのものを機械的に完全検証しようとしない。F# に落としてコンパイルすることで、最小限の矛盾検出を行う。
