# 開発スタイル

TDD で開発する（探索 → Red → Green → Refactoring）。
KPI やカバレッジ目標が与えられたら、達成するまで試行する。
不明瞭な指示は質問して明確にする。

# コード設計

- 関心の分離を保つ
- 状態とロジックを分離する
- 可読性と保守性を重視する
- コントラクト層（API/型）を厳密に定義し、実装層は再生成可能に保つ
- 静的検査可能なルールはプロンプトではなく、その環境の linter か ast-grep で記述する


# 言語

- 本リポジトリの主要言語は日本語。ドキュメント・コミットメッセージ・コメントは日本語で記述する
- ただしコード上の識別子（型名・関数名・変数名）は英訳して使う（命名規約セクション参照）

# PoC固有ルール

## 改修仕様の受け付け（Loop Engineering）

機能追加・改修の仕様は `specs/requests/<依頼ID>/` に置かれる（書き方・手順は [`specs/README.md`](specs/README.md)）。改修セッションは対象依頼の仕様一式を読み、次の順で作業する:

0. **トラック確認**: 依頼のトラック（`specs/README.md` §0）を確認する。B で仕様文書が未起案なら先に AI が既存仕様・defaults から起案して人間承認を得る（例・反例の期待値は勝手に確定させず未決事項で問う）。C は仕様書なしで着手し、oasdiff 差分ゼロ + characterization テストで挙動不変を証明する
1. **ゲート先行（Red）**: 各仕様の「品質ゲート化対応表」に従い、DSL / openapi.yaml 差分と失敗するテストを先に追加し、テスト ID を仕様に転記する
2. **契約差分の承認待ち**: openapi.yaml / イベントスキーマの差分が新規エンドポイントまたは破壊的変更を含む場合、差分コミットを push してエンジニアの承認を得るまで実装に進まない（`specs/README.md` ③）
3. **実装（Green）**: `scripts/verify.sh` が緑になるまで実装する

作業中に新しい業務判断（境界・権限・状態の変更）が出てきたらトラックを昇格し、不足する仕様欄を起案して承認に戻す（`specs/README.md` §0 の昇格規則）。

仕様に無い設計判断（コンポーネント分割・hook・DB スキーマ等）は本書の規約とゲートの範囲で自律決定してよい。仕様の矛盾・欠落は実装で埋めず、依頼書の「未決事項」に追記して質問に戻す。

## テストハーネス

新規テスト（`apps/api-fsharp/tests/SalesManagement.Tests/`）は `Support/*`（`ApiFixture` / `HttpHelpers` / `ProblemDetailsAssert` / `RequestBuilders` / `Generators`）を使うこと。コピペ禁止。

## ファイル配置規約

```
apps/api-fsharp/
├── src/SalesManagement/
│   ├── Domain/Types.fs          # DSLから生成した型定義
│   ├── Domain/Workflows.fs      # behaviorの実装（純粋関数）
│   ├── Infrastructure/          # DB永続化（Donald）
│   └── Api/                     # Giraffeルーティング
├── tests/SalesManagement.Tests/
└── tools/Migrator/

apps/api-kotlin/  (将来予定)
├── src/main/kotlin/salesmanagement/
│   ├── domain/Types.kt          # DSLから生成した型定義
│   ├── domain/Workflows.kt      # behaviorの実装（純粋関数）
│   ├── infrastructure/          # DB永続化（Exposed）
│   └── api/                     # Ktorルーティング
└── src/test/kotlin/salesmanagement/
```

## 命名規約

- DSLの日本語名を英訳してコードに使う
- 型名: PascalCase（例: `在庫ロット` → `InventoryLot`）
- フィールド/変数: camelCase（例: `製造完了日` → `manufacturingCompletedDate`）
- 関数名: camelCase、動詞始まり（例: `製造完了を指示する` → `completeManufacturing`）
- エラー型: `〜Error` suffix（例: `製造完了指示エラー` → `ManufacturingCompletionError`）

## DSL解釈ルール

- `data X = A OR B` → 判別共用体 / sealed interface
- `data X = A AND B` → レコード / data class
- `behavior F = Input -> Output OR Error` → 純粋関数（副作用なし）
- `List<X> // 1件以上` → NonEmptyList として扱う
- `フィールド?` → Option / nullable
- エラー型の内部構造はDSLに定義がないため、実装時に判断する

# CI / SARIF 集約

後続のタスクを追加するときは以下を前提とする：

- すべての CI ツールの結果は `ci-results/sarif/<tool>.sarif` に出力し、`ci-results/merged.sarif` に統合する
- 失敗の自己分析は `Stop` フック（`.claude/scripts/sarif-to-lessons.py`）がリポジトリルートの `LESSONS.md`（マーカ間の自動生成領域）に記録する。恒久対応が済んだ教訓は `LESSONS.md` から削除する
- コミット前の統合ゲートは `scripts/verify.sh`（backend + frontend を変更スコープで自動判定、fail-closed）。ralph-orchestrator のデフォルト verify もこれに委譲する。重量級検査（ZAP / Schemathesis / SBOM 等）は従来どおり `apps/api-fsharp/ci.sh`
- verify のマージゲート内訳（issue #9 Tier1 で拡充）: backend = build(-warnaserror) / fantomas / FSharpLint / テスト数 + カバレッジラチェット / アーキテクチャテスト / Broker レス Pact 検証 / openapi レスポンス照合（`Support/OpenApiValidation.fs`）、frontend = typecheck / biome / Spectral / 生成コードドリフト / カバレッジラチェット / MSW 契約ドリフト検査（`tests/support/contract-guard.ts`）/ `pnpm build`、repo 横断（スコープ不問）= gitleaks / oasdiff 破壊的変更 / shellcheck / actionlint / bats（スコープ判定の単体テスト）
- GitHub Actions: push / PR の軽量ゲートは `.github/workflows/verify.yml`（`scripts/verify.sh` に委譲）、nightly の重量ゲートは `.github/workflows/nightly.yml`（`ci.sh` フル実行、失敗時は `ci-nightly` ラベルの Issue にエスカレーション）

## 関連ディレクトリ

```
ci-results/                    # gitignore 対象
├── sarif/                     # ツール別 SARIF
│   ├── gitleaks.sarif
│   ├── trivy.sarif
│   ├── detekt.sarif
│   ├── fsharp-build.sarif
│   ├── sonar.sarif
│   └── zap.sarif
├── merged.sarif               # マージ済み（エージェント参照用）
├── sbom-fsharp.cdx.json       # SBOM
├── sbom-kotlin.cdx.json       # SBOM
└── renovate.log               # 依存更新候補

.claude/                       # Claude Code フック / 同梱プラグイン
├── settings.json
├── scripts/
│   ├── start-trace.py         # SessionStart: trace ID 生成
│   ├── emit-otel.py           # PostToolUse: OTel スパン送信
│   └── sarif-to-lessons.py    # Stop: LESSONS.md 自動更新
└── plugins/
    └── ralph-orchestrator/    # DAG ベース multi-session RALPH (詳細は README 参照)
```

## ralph-orchestrator の利用

本リポジトリには `.claude/plugins/ralph-orchestrator/` を同梱してある。DAG ベースで `.ralph/tasks.toml` のタスクを並列実行し、verify 緑なら main に自動マージする。利用するには:

1. プロジェクトルートに `.ralph/tasks.toml` と `.ralph/verify/<task-id>.sh` を作る（詳細は `.claude/plugins/ralph-orchestrator/README.md` の "プロジェクト要件" 参照）
2. `.gitignore` に `.ralph/state.json` / `.ralph/state.lock` / `.ralph/logs/` / `.ralph/orchestrator.pid` 等のランタイム生成物を追加する
3. `/ralph-orch start` でバックグラウンド起動

worker 用のライフサイクル契約は `.claude/plugins/ralph-orchestrator/skills/ralph-task/SKILL.md` に本リポジトリ (F#/.NET) 向けに調整済み。

# スキル作成

新規 skill を作るとき、配置先を次の指針で決める:

- **project 固有** (`<repo>/.claude/skills/<skill-name>/SKILL.md`): 特定 repo のドメイン知識・規約・ファイルレイアウトに依存し、他 repo で使う見込みがない
- **グローバル** (`~/.claude/skills/<skill-name>/SKILL.md`): 言語・ツール横断、複数 repo で再利用可能、運用ノウハウ
- **判断不能なとき**: ユーザーに「project 固有かグローバルか」を質問してから作成（理由: 後から移動するとフロントマター参照や呼び出し側のスキル名が壊れやすい）

スキルファイルは YAML frontmatter（`name`, `description`）を先頭に記述する。Claude Code は `.claude/skills/` 配下を自動検出し、`Skill` ツール経由（または `/<skill-name>` スラッシュコマンド）で起動できる。サブエージェントを別途定義する場合は `.claude/agents/<agent-name>.md`（project）または `~/.claude/agents/<agent-name>.md`（user）に Markdown + frontmatter で配置する。

# API Fuzz (Schemathesis)

`apps/api-fsharp/ci.sh` は ZAP 直後に Schemathesis を回し、`openapi.yaml` から property-based の入力を生成して `http://localhost:5000` を叩く。固定 seed `42` / `-n 200` / `--request-timeout 2.0` で反復可能。`SCHEMATHESIS_ENABLED=0` で全体スキップ可能（高速モード）。事前状態を要するエンドポイント（`POST /sales-cases/{id}/contracts` など appraised 必須のもの、ロット状態遷移、reservation/consignment 多段フロー）は `apps/api-fsharp/schemathesis-hooks.py` の `before_load_schema` フックで raw schema から物理的に除外している。出力は `ci-results/schemathesis-junit.xml` / `ci-results/sarif/schemathesis.sarif` / `ci-results/schemathesis.tar.gz` の 3 点。`scripts/junit-to-sarif.py` が JUnit XML を SARIF v2.1.0 に変換し、`merged.sarif` にも統合される。発見は当面 `warning` レベル扱いで CI を落とさない（信号品質が安定したら `error` に昇格する）。

# 失敗から学んだこと

CI 失敗の頻出パターン（未消化の教訓）は [`LESSONS.md`](LESSONS.md) に集約している。
**作業開始時に `LESSONS.md` の未消化教訓に目を通し、同じ失敗を繰り返さないこと。**
Stop フックが自動更新するため、本ファイルには追記しない。
恒久対応（linter / ast-grep / verify スクリプト / スキーマ修正）が済んだ教訓は `LESSONS.md` から削除する。
対応不要と判断した検出は `LESSONS.md` の `lessons:ignore` ディレクティブで恒久的に無視できる。
