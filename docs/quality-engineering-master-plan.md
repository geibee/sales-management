# 品質向上・開発効率向上 全体計画

作成日: 2026-05-13

## 目的

本計画は、現在のリポジトリに対して品質向上と開発効率向上の施策を領域別ブランチで順番に導入するためのマスタープランである。オーバーエンジニアリングを許容し、短期的な導入容易性よりも、検証可能性、再現性、監査可能性、将来の自動化余地を優先する。

主な対象は次のとおり。

- F# API のドメインロジック、永続化、API ルート、バッチ処理
- React/TanStack Router フロントエンド
- OpenAPI、Pact、Schemathesis、FsCheck、ZAP、SARIF 集約を含む CI
- 依存関係、ビルド成果物、SBOM、署名、provenance を含む supply chain
- 開発者体験、delivery performance、AI 支援開発の効果測定

## 基本方針

- 各領域は `main` から専用ブランチを切って進める。
- 共有基盤が必要な場合は、先に基盤ブランチを `main` に統合してから後続ブランチを切る。
- 各ブランチは、設計ドキュメント、実装、検証コマンド、CI/SARIF 連携方針を含める。
- 新規 CI ツールの結果は原則として `ci-results/sarif/<tool>.sarif` に出し、`ci-results/merged.sarif` に統合する。
- まずは report-only で導入し、信号品質が安定してから blocking gate に昇格する。
- 自動生成テストや LLM 生成テストは、人間レビュー、既存テスト通過、mutation score 改善を採用条件にする。
- メトリクスは個人評価に使わず、リポジトリと開発プロセスの改善対象を見つけるために使う。

## ブランチ運用

ブランチ命名は `docs/`、`quality/`、`security/`、`testing/`、`formal/`、`devex/` を用途で使い分ける。

| 領域 | 想定ブランチ | 主な成果物 |
| --- | --- | --- |
| 全体計画 | `docs/quality-engineering-master-plan` | 本ドキュメント |
| Schemathesis 改善 | `testing/schemathesis-signal-quality` | OpenAPI 制約強化、hook 整理、SARIF severity 方針 |
| FsCheck 改善 | `testing/fscheck-domain-properties` | generator 整理、境界値 property、状態遷移 property |
| ミューテーションテスト | `testing/stryker-net-mutation-baseline` | Stryker.NET 設定、対象範囲、report-only CI |
| LLM 生成テスト | `testing/llm-testforge-feedback-loop` | 生成ループ、採点、隔離実行、採用基準 |
| TLA+ | `formal/tla-plus-state-specs` | 状態機械 spec、TLC 実行、CI 連携 |
| Alloy | `formal/alloy-domain-structure` | ドメイン制約 model、反例探索、DSL 連携 |
| Supply chain | `security/slsa-cosign-sbom-vex` | SLSA provenance、cosign 署名、SBOM/VEX、検証手順 |
| OpenAPI/契約統制 | `quality/api-contract-governance` | Spectral/OpenAPI diff/Pact/Schemathesis の役割分担 |
| Observability | `quality/otel-trace-regression` | OTel span 設計、trace-based regression、SLO 雛形 |
| DevEx/DORA/SPACE | `devex/dora-space-measurement` | delivery 指標、CI 待ち時間、レビュー滞留、アンケート |
| CI 品質ゲート統合 | `quality/ci-quality-gates` | report-only から blocking への昇格ルール |

## 導入順序

### 0. 計画とベースライン

目的: 以後の施策を比較可能にするため、まず現状を固定する。

作業:

- 本ドキュメントを `main` に統合する。
- 既存 CI、PBT、Schemathesis、ZAP、SARIF、SBOM 生成の現状を棚卸しする。
- 既存の実行時間、失敗件数、警告件数、PBT 件数を記録する。

完了条件:

- 全体計画が `main` に存在する。
- 各領域ブランチの着手順と完了条件が明文化されている。

### 1. DevEx/DORA/SPACE の最小計測

目的: 施策の効果を「速くなった気がする」ではなく、開発フローと品質の指標で追跡する。

作業:

- CI 実行時間、テスト種別ごとの実行時間、SARIF 件数、Schemathesis 件数を記録する。
- commit から CI 完了まで、PR 作成から merge まで、レビュー待ち時間を収集する。
- DORA の change lead time、deployment frequency、change fail rate、failed deployment recovery time、deployment rework rate の計測方法を定義する。
- SPACE の satisfaction/performance/activity/collaboration/efficiency のうち、リポジトリから自動取得できる項目とアンケート項目を分ける。

成果物:

- `docs/devex-dora-space-measurement-plan.md`
- `apps/api-fsharp/scripts/` または `scripts/` 配下の集計スクリプト
- `ci-results/devex/` 配下のローカル集計出力

完了条件:

- 少なくとも CI 時間、テスト時間、SARIF 件数、PR 滞留時間の取得方法がある。
- 個人評価に使わないという利用制約が文書化されている。

### 2. Schemathesis の信号品質改善

目的: API fuzz のノイズを下げ、OpenAPI 契約違反を信頼できる signal にする。

作業:

- 429、認証、状態遷移前提、schema mismatch による既知ノイズを分類する。
- `openapi.yaml` に required、format、enum、minimum、maximum、pattern、nullable を追加する。
- `schemathesis-hooks.py` の除外理由を endpoint 単位で明文化する。
- report-only の SARIF を warning に維持しつつ、再現 ID、seed、縮約ケースを保存する。

成果物:

- `docs/schemathesis-signal-quality-plan.md`
- `apps/api-fsharp/openapi.yaml`
- `apps/api-fsharp/schemathesis-hooks.py`
- `apps/api-fsharp/scripts/junit-to-sarif.py` の必要最小限の調整

完了条件:

- Schemathesis の既知 warning が分類されている。
- schema 違反を受理する API が減っている。
- CI は既存の `SCHEMATHESIS_ENABLED=0` を維持しつつ通常経路で再現可能である。

### 3. FsCheck の domain property 拡張

目的: F# の純粋関数層を、例示テストではなく性質で守る。

作業:

- `Support/Generators.fs` に generator を集約し、重複した任意値生成を削減する。
- `Domain/SmartConstructors.fs` の境界値 property を追加する。
- lot、sales case、reservation、consignment の状態遷移 property を追加する。
- DTO/domain 変換が情報を失わない property を追加する。

成果物:

- `docs/fscheck-domain-property-plan.md`
- `apps/api-fsharp/tests/SalesManagement.Tests/*PropertyTests.fs`
- `apps/api-fsharp/tests/SalesManagement.Tests/Support/Generators.fs`

完了条件:

- `dotnet test apps/api-fsharp/tests/SalesManagement.Tests --filter "Category=PBT" --no-restore` が通る。
- seed と試行回数の方針が文書化されている。
- generator が過度に実装詳細へ依存していない。

### 4. Stryker.NET による mutation baseline

目的: テストがコードを実行しているだけでなく、人工バグを検出できるかを測る。

作業:

- F# 対応範囲を前提に、まず pure domain module に対象を絞る。
- mutation score の初期値を取得する。
- timeout、除外 mutant、生成コード除外、report-only CI を設定する。
- HTML/JSON/SARIF 変換の保存場所を決める。

成果物:

- `docs/mutation-testing-adoption-plan.md`
- `apps/api-fsharp/stryker-config.json`
- `apps/api-fsharp/scripts/stryker-to-sarif.py`

完了条件:

- 対象範囲を絞った Stryker.NET がローカルで再現可能である。
- 初期 mutation score が記録されている。
- CI では report-only として扱われる。

### 5. LLM 生成テストの TestForge 型 feedback loop

目的: LLM にテストを一発生成させるのではなく、実行結果、coverage、mutation score を使って反復改善する。

作業:

- 対象ファイルを pure domain function に限定する。
- 生成テストは隔離ディレクトリに出し、既存テストへ直接混ぜない。
- ループ入力に、対象コード、既存テスト、失敗ログ、未カバー行、生存 mutant を渡す。
- 採点は test pass、coverage 差分、mutation score 差分、可読性レビューで行う。
- 採用時は人間が `Support/*` 利用、命名、DSL との整合性を確認する。

成果物:

- `docs/llm-generated-test-feedback-loop.md`
- `tools/llm-testgen/` または `apps/api-fsharp/scripts/llm-testgen-*`
- 生成テストの一時出力ディレクトリ

完了条件:

- 生成テストが既存テストを壊さない。
- mutation score が改善しないテストは採用しない。
- LLM 出力を無審査で commit しないルールが文書化されている。

### 6. TLA+ による状態・並行性仕様

目的: 実装テストだけでは見落としやすい状態遷移、楽観ロック、outbox、batch progress の不変条件を検証する。

作業:

- `specs/tla/` を追加する。
- `LotLifecycle`、`SalesCaseLifecycle`、`OptimisticLock`、`OutboxProcessing`、`BatchChunkProgress` の順に spec を追加する。
- TLC 実行コマンドと counterexample の読み方を文書化する。
- 仕様と F# property test の対応表を作る。

成果物:

- `docs/tla-plus-adoption-plan.md`
- `specs/tla/*.tla`
- `specs/tla/*.cfg`
- `scripts/tla-check.sh`

完了条件:

- 最初の TLA+ spec が TLC で検証できる。
- 反例が出た場合に、対応する FsCheck または integration test を追加する運用がある。

### 7. Alloy による構造制約・DSL 整合性検証

目的: TLA+ が時系列の振る舞いを扱う一方で、Alloy はドメイン構造、関係、多重度、到達不能状態を検証する。

作業:

- `specs/alloy/` を追加する。
- DSL の `AND`、`OR`、`List<X> // 1件以上`、optional を Alloy model に落とす。
- lot、sales case、reservation、consignment の関係制約を model 化する。
- F# 型、OpenAPI schema、DB migration との不整合検出観点を整理する。

成果物:

- `docs/alloy-domain-structure-plan.md`
- `specs/alloy/*.als`
- DSL から Alloy への対応表

完了条件:

- 少なくとも 1 つの到達不能または矛盾状態を Alloy で探索できる。
- Alloy の反例を F# test または DSL 修正に接続する方針がある。

### 8. OpenAPI/契約統制

目的: Pact、OpenAPI、Schemathesis、frontend generated contract の責務を分け、契約の drift を早期検出する。

作業:

- OpenAPI lint を追加する。
- OpenAPI diff で破壊的変更を検出する。
- Pact provider test と frontend contract test の役割を文書化する。
- Schemathesis は「schema から生成した入力で API が契約を満たすか」に集中させる。

成果物:

- `docs/api-contract-governance.md`
- OpenAPI lint 設定
- OpenAPI diff 実行スクリプト
- SARIF 変換

完了条件:

- OpenAPI の破壊的変更が CI で検出できる。
- Pact と Schemathesis の失敗理由が混ざらない。

### 9. Supply chain hardening

目的: 依存関係とビルド成果物を、追跡可能、検証可能、監査可能にする。

作業:

- backend と frontend の SBOM を生成し、既存 `ci-results/` 方針に合わせる。
- VEX を導入し、検出された CVE が本製品で exploit 可能かを記録する。
- SLSA provenance を生成し、どの commit、workflow、builder、依存から成果物が作られたかを残す。
- cosign でコンテナ、SBOM、provenance に署名する。
- 署名検証、provenance 検証、SBOM/VEX 検証を release 前 gate にする。

成果物:

- `docs/supply-chain-hardening-plan.md`
- GitHub Actions または CI スクリプト
- `ci-results/sbom-*.cdx.json`
- `ci-results/vex-*.cdx.json`
- provenance attestation
- cosign verification script

完了条件:

- 成果物から source commit と build workflow を辿れる。
- 署名されていない成果物を release しない運用が文書化されている。
- SBOM と VEX が同じ dependency snapshot に対応している。

### 10. Observability と trace-based regression

目的: テストではなく実行時の振る舞いから、退行と運用上の劣化を検出する。

作業:

- 重要 API、DB 操作、外部価格 API、batch、outbox に span 命名規約を入れる。
- trace attribute の PII 混入ルールを定義する。
- integration test で重要 span が出ているかを検証する。
- SLO 候補と error budget を文書化する。

成果物:

- `docs/observability-trace-regression-plan.md`
- OTel span naming guide
- trace assertion test
- SLO draft

完了条件:

- 重要 workflow の trace が相関 ID で辿れる。
- 退行検出に使う span と attribute が固定されている。

### 11. CI 品質ゲート統合

目的: 個別導入したツールを、使える順に blocking gate へ昇格する。

作業:

- report-only、warning gate、blocking gate の 3 段階を定義する。
- SARIF severity と CI exit code の対応を統一する。
- flaky 判定、再実行、known issue の期限付き抑制を設計する。
- 低速ジョブを daily、PR、release の実行層に分ける。

成果物:

- `docs/ci-quality-gates.md`
- `apps/api-fsharp/ci.sh` の段階制御
- SARIF merge 方針の更新

完了条件:

- どのツールが PR blocking か、daily only か、release gate かが明文化されている。
- warning が放置されないよう、期限または owner を持つ。

## 横断メトリクス

| メトリクス | 用途 | 初期 gate |
| --- | --- | --- |
| PBT 件数 | 性質ベースの保護範囲 | trend |
| Schemathesis finding 件数 | API 契約ノイズと実欠陥 | warning |
| Mutation score | テストの欠陥検出力 | report-only |
| Survived mutant 件数 | 追加すべきテスト候補 | report-only |
| SARIF 件数 | 静的・動的検査の検出傾向 | warning |
| CI wall time | feedback loop の遅さ | trend |
| PR lead time | 開発フローの滞留 | trend |
| Review wait time | collaboration bottleneck | trend |
| Change lead time | DORA throughput | trend |
| Change fail rate | DORA instability | trend |
| Deployment rework rate | 手戻り比率 | trend |

## 採用しない測定

次の値は、誤用されやすいため単独の評価指標にしない。

- 個人別 commit 数
- 個人別 PR 数
- 生成コード行数
- LLM 利用回数
- coverage の単独目標
- issue close 数の単独比較

これらは activity の補助情報にはなるが、品質、安定性、flow、well-being と組み合わせなければ意思決定に使わない。

## 参照する外部仕様・研究

個別ブランチの着手時点で最新版を再確認する。

- SLSA v1.2 Build Provenance
- Sigstore cosign
- CycloneDX SBOM/VEX
- DORA software delivery performance metrics
- SPACE developer productivity framework
- DevEx framework
- TestForge: Feedback-Driven, Agentic Test Suite Generation
- Stryker.NET F# support documentation
- FsCheck documentation
- Schemathesis documentation
- TLA+ documentation
- Alloy 6 documentation

## 当面の推奨順

最初の実装順は次を推奨する。

1. `devex/dora-space-measurement`
2. `testing/schemathesis-signal-quality`
3. `testing/fscheck-domain-properties`
4. `testing/stryker-net-mutation-baseline`
5. `testing/llm-testforge-feedback-loop`
6. `formal/tla-plus-state-specs`
7. `formal/alloy-domain-structure`
8. `quality/api-contract-governance`
9. `security/slsa-cosign-sbom-vex`
10. `quality/otel-trace-regression`
11. `quality/ci-quality-gates`

理由は、まず測定基盤を作り、次に既存の PBT/API fuzz を改善し、その後に mutation score と LLM 生成テストを接続する方が、後続施策の効果を測りやすいためである。formal methods と supply chain hardening は独立性が高いが、CI 実行時間と gate 設計への影響が大きいため、report-only の運用が安定してから blocking に昇格する。
