# 決定的品質ゲートのギャップ分析と拡充提案

Loop Engineering（Osmani ほか, 2026 / cobusgreyling/loop-engineering）の観点による、
本リポジトリの「決定的（deterministic）な品質ゲート」の棚卸しと不足の指摘、および追加候補の全量列挙。

> 参照した原則（Stripe Minions の教訓）:
> **「決定的ロジックで解けるものは確率的なモデルに絶対に渡さない。その線引きがループの信頼性を決める」**。
> AI レビュー（非決定的ゲート）の前に、決定的ゲートを限界まで敷き詰めることが自動マージ・自動開発の前提になる。
> 本ドキュメントは意図的にオーバーエンジニアリング側に倒して「追加しうるものすべて」を列挙する。採否は優先度表で判断する。

## 0. 現状の評価サマリ

既存ゲートはすでに水準が高い。特に以下は Loop Engineering の教科書どおり:

- `scripts/verify.sh` の **fail-closed 設計**（ツール欠如=失敗、未分類パス=全検証）
- frontend の **カバレッジラチェット** / backend の **テスト数ラチェット**
- **生成コードドリフト検査**（`check:contracts-drift`）と Spectral 契約 lint
- Lot 集約の **ステートフル PBT**（モデルベースで実 API を検証）
- nightly の重量ゲート（ZAP / Schemathesis / gitleaks / Trivy / SBOM）+ SARIF 集約 + `LESSONS.md` 学習ループ

一方で、ギャップは大きく 3 系統に分類できる:

1. **fail-open な穴** — 存在するのに条件次第で「スキップして緑」になるゲート（Pact、Schemathesis warning 扱い、backend カバレッジ未ラチェット）
2. **カバレッジの空白** — ゲート自体が無い領域(フロント PBT、認可マトリクス、マイグレーションスキーマ、CI スクリプト群そのもの)
3. **マージゲートに昇格できるのに nightly 止まりのもの** — アーキテクチャテスト、FSharpLint、gitleaks 等

---

## 1. 【最優先】既存ゲートの fail-open な穴を塞ぐ

ループ自動化では「検証されなかったものが緑になる」ことが最大のリスク。新規テスト追加より先にここを塞ぐ。

| # | 穴 | 現状 | 対策 |
|---|---|---|---|
| 1-1 | **Pact provider 検証が silent skip** | Broker 不在（GitHub ランナーでは常に不在）だと丸ごとスキップ | PactNet は **ローカル pact ファイル直読みで Broker レスで検証可能**。`pacts/*.json` を直接読む provider テストを Broker 不要の通常テスト（Category 外）にし、verify のマージゲートに昇格 |
| 1-2 | **backend カバレッジが記録のみ** | coverage.json は取るが退行しても緑 | frontend と同じ **baseline JSON + ラチェットスクリプト**を backend にも実装（coverlet の line/branch を比較）。テスト数ラチェットは「中身の薄いテストで数だけ稼ぐ」ゲーミングに弱いので併用必須 |
| 1-3 | **`--warnaserror` が CLI フラグのみ** | fsproj/`Directory.Build.props` に設定がなく、IDE・素の `dotnet build` では警告が素通り | `apps/api-fsharp/Directory.Build.props` に `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`（+ `<WarningLevel>`、FS0025 網羅性警告の明示）を置き、ビルド経路によらず同一の強制にする |
| 1-4 | **Schemathesis が warning 扱い** | 発見が CI を落とさない（AGENTS.md にも「安定したら error 昇格」と明記済み） | `LESSONS.md` の既知 5 件をトリアージ（openapi.yaml の examples 追加、`POST /lots` の schema-violation 修正）した上で **error 昇格**。あわせて hooks で除外中のステートフル系エンドポイントは Schemathesis の stateful/links 機能か専用フックで復帰させる |
| 1-5 | **フロントの `pnpm build` が PR ゲートに無い** | typecheck のみで Vite ビルド破壊（動的 import、asset 解決、Tailwind 等）は検出されない | verify.sh frontend に `pnpm build` を追加 |
| 1-6 | **E2E が nightly のみ** | PR マージ時点で E2E 未実行 | 少なくとも **MSW/スタブ backend での smoke E2E**（決定的・高速）を PR ゲートへ。実 backend E2E は nightly 継続 |
| 1-7 | **バッチ系テストが外部 Postgres 前提** | `BatchFixture` は DATABASE_URL の外部 DB 依存（Testcontainers ではない）。環境差で挙動が変わる | Testcontainers に統一 or verify.sh で DB 前提を fail-closed に明示 |

## 2. マージゲートへの昇格（既に存在し、速く、決定的なもの）

nightly は「発見」には効くが自動マージは守らない。実行時間が許すものは verify.sh に移す。

- **アーキテクチャテスト（ArchUnitNET, Category=Architecture）** — 数秒で終わる。verify.sh backend に追加
- **FSharpLint** — verify.sh backend に追加（SARIF 出力は nightly のままでよい。verify では exit code ゲート）
- **gitleaks** — 高速。verify.sh の共通ステップ（スコープ不問）に追加。秘密情報は「一度 push されたら終わり」なので PR 前ゲートに置く価値が最も高い
- **`dotnet list package --vulnerable` / `pnpm audit --prod`** — 数秒。週次 Renovate だけでなく verify で新規導入依存の既知脆弱性を即検出

## 3. バックエンド: Domain 層（`src/SalesManagement/Domain/`）

純粋関数層はテスト容易性が最も高いのに、直接テストが最も薄い。

| 種別 | 対象 | 内容 |
|---|---|---|
| 例示ベース単体テスト | `SmartConstructors.fs` | `PositiveInt`/`NonEmptyString`/`Amount` 等の **境界値テーブルテスト**（0, -1, 1, max, 空白のみ, 全角空白, サロゲートペア）。現在直接テストゼロ |
| 例示ベース単体テスト | `Errors.fs` / `Events.fs` | エラー型→ProblemDetails 変換の全ケース網羅、イベントのシリアライズ round-trip |
| PBT（境界・不正値） | 全 smart constructor | `tryCreate` の成功/失敗が仕様の述語と一致する property（docs/pbt-fscheck-improvement-proposal.md F3 の実装） |
| PBT（代数法則） | `LotWorkflows` ほか 4 workflow | 冪等性・可換性・不変量（例: 状態遷移後も lotNumber 不変、金額合計の保存則、調整率 0.9–1.1 の閉包） |
| **ステートフル PBT の横展開** | SalesCase / ReservationCase / ConsignmentCase | Lot で実装済みのモデルベース PBT（`LotStateMachinePropertyTests.fs`）を残り 3 集約へ。多段フロー（査定→成約、預託→引出）はここが本丸 |
| DSL↔コード整合ゲート | `dsl/domain-model.md` ↔ `Domain/*.fs` | `behavior` 名（英訳規約適用後）と F# 関数の存在を突合する**決定的スクリプト**（grep レベルで可）。現在 DSL 整合は「ビルドが通るか」の間接検証のみで、DSL に追加された behavior の実装漏れは検出されない |
| 網羅性の静的強制 | 全 match 式 | FS0025/FS0026 を error 固定（1-3 の props で実現）。判別共用体へのケース追加が全 match を強制的に赤にする = DSL 駆動開発の要 |

## 4. バックエンド: Infrastructure 層（DB / 外部連携）

| 種別 | 対象 | 内容 |
|---|---|---|
| Round-trip property | 全 Repository（7 つ） | `save >> load = id` の PBT（Testcontainers）。ドメイン型⇔行マッピングの欠落フィールドを決定的に検出 |
| **SQL 静的整合検査** | Donald / 生 NpgsqlCommand の全 SQL | マイグレーション適用済み DB に対し全 SQL 文を `PREPARE`（実行せず構文+列存在検証）する網羅テスト。SQL 文字列とスキーマのドリフトをテスト実行前に一括検出 |
| スキーマスナップショット | `migrations/` 全 16 ファイル | `pg_dump --schema-only` の正規化ダンプをリポジトリにコミットし、CI で再生成して diff ゲート。**マイグレーション追加の影響が PR diff で見える**ようになる（現状は batch/lot テーブルの情報スキーマ検査のみ） |
| マイグレーション実行特性 | Migrator (DbUp) | (a) 2 回適用の冪等性テスト、(b) **重複番号 007/008/009 の適用順序を固定するテスト**（現状はファイル名ソート依存の暗黙挙動で無検証）、(c) 空 DB→最新とテスト fixture の一致 |
| 並行性テスト | 楽観ロック全集約 | 並列更新で必ず片方 409 になる決定的テスト（Lot は PBT にあり。他集約に横展開） |
| Outbox 保証 | `OutboxRepository`/`OutboxProcessor` | at-least-once（クラッシュ再開後の再送）、毒メッセージの隔離、順序性の property |
| 外部 API 耐性 | `ExternalPricingClient` (Polly) | タイムアウト/リトライ/サーキットブレーカの決定的検証（WireMock.Net の遅延・故障注入。compose の WireMock slow スタブはあるがテスト未接続） |
| LocalStack 統合 | SQS / Step Functions / EventBridge | `localstack/init/setup.sh` の産物を assert する統合テスト（キュー存在、ステートマシン定義、ルールのcron式）。現状ゼロ |
| 時刻の決定性強制 | 全層 | ast-grep で `DateTime.Now`/`DateTime.UtcNow` 直呼びを禁止し clock 注入を強制（テストの決定性の土台） |

## 5. バックエンド: API 層（`Api/`）

| 種別 | 対象 | 内容 |
|---|---|---|
| **レスポンススキーマ全数検証** | 全統合テスト | ApiFixture に「全レスポンスを openapi.yaml と照合する DelegatingHandler」を仕込む。**Schemathesis がステートフル系を除外している穴を、既存統合テストのトラフィックで埋める**最重要ゲート。既存テストを書き換えず検証面だけ増える |
| **契約カバレッジラチェット** | openapi.yaml 35 operationId | 統合テストが叩いた operationId を記録し「テスト未到達の operation 一覧」を baseline 化してラチェット。API 追加時にテスト追加を機械的に強制 |
| 認可マトリクス | 全エンドポイント | openapi.yaml から operation を列挙し「未認証→401 / 権限不足→403」を全数生成するテーブルテスト。**spec 由来の列挙なので新エンドポイントの検査漏れが構造的に起きない** |
| ProblemDetails 全数検証 | 全エラーレスポンス | 上記ハンドラで 4xx/5xx が RFC9457 スキーマに常に適合することを検証 |
| CSV 契約 | `LotCsvExport.fs` | ゴールデンファイルテスト（列順・エンコーディング・BOM）、CSV インジェクション（`=CMD()` 等の先頭文字エスケープ）検査 |
| HTTP セマンティクス | ルーティング全体 | 未定義メソッド→405、未知 Content-Type→415、巨大ボディ→413 の網羅テスト |
| アーキテクチャルール追加 | ArchUnitNET | 「**Api 層から Donald/Npgsql 直接参照禁止**」ルール追加（現状 `Api/SalesCaseDetailRoutes.fs` が Donald を直接使用しており、既存 4 ルールでは検出されない）。ほか「エラー型は `Error` suffix」「Handlers は `HttpHandler` を返す」等の命名・型規約 |

## 6. 契約層（openapi.yaml / Pact）

| 種別 | 内容 |
|---|---|
| **破壊的変更ゲート（oasdiff）** | `oasdiff breaking main..HEAD apps/api-fsharp/openapi.yaml` を verify に追加。後方互換を壊す spec 変更（必須フィールド追加、enum 削除、型変更）を PR で機械検出。エージェントループには特に効く（AI は契約を「都合よく」変えがち） |
| Pact consumer テストの実体化 | 現在 `pacts/frontend-sales-management.json` は**手書きで 2 interaction のみ**。pact-js（@pact-foundation/pact）で frontend の実クライアントコードから生成し、手書きドリフトを排除。対象 interaction を主要フロー全体に拡大 |
| Broker レス Pact 検証 | 1-1 のとおり provider 検証をファイル直読みで verify に組込み。Broker は「あれば使う」オプションに格下げ |
| examples 検証 | Spectral カスタムルール or `openapi-examples-validator` で「全スキーマに example があり、schema に適合する」を強制（Schemathesis の `No examples in schema` skip の恒久対応） |
| enum 重複検査の復活 | `duplicated-entry-in-enum` は engine バグで off。spectral 更新で復活 or 簡易スクリプトで代替（現在「手動確認」= 非決定的） |

## 7. フロントエンド（`apps/frontend/`）

| 種別 | 対象 | 内容 |
|---|---|---|
| **PBT（fast-check）** | rate 換算 / 合計 / フォーマッタ / version | `docs/frontend-component-page-test-plan.md` Phase 9a–9f が**設計済み・未実装**（fast-check 未導入）。FE-PBT-RATE-001 ほか 10 property をそのまま実装するのが最短 |
| **MSW ↔ 契約ドリフト検査** | `tests/support/` 全ハンドラ | MSW ハンドラのレスポンスを **生成済み zod スキーマ（generated.ts）で parse してから返す**共通ラッパを導入。モックが契約から乖離した瞬間にテストが赤になる。契約駆動の要 |
| ルータ統合テスト | `src/routes/*`（現在 0%） | test-plan Phase 6（FE-NAV-*）。createMemoryHistory による実ルータ遷移テスト |
| 未テスト部の単体テスト | `ui-store.ts` / `lib/utils.ts` / hooks 10 本 / `HealthIndicator` / `RoleBadge` / `SwaggerLink` | SWR フックはミューテーション URL・キー・エラーパスを直接検証 |
| a11y 自動検査 | 全ページ | `jest-axe`（vitest 側）+ `@axe-core/playwright`（E2E 側）。現状は手書き aria assertion のみで、ルール網羅は不可能。`aria-describedby` TODO の実装もここで拾う |
| ビジュアルリグレッション | 主要ページ・organisms | Playwright `toHaveScreenshot`（chromium 固定・アニメーション無効化で決定化）。過剰なら organisms のみ |
| Storybook + インタラクションテスト | atoms / molecules | atoms 11 個が無テスト。story を書けば play function + a11y addon + VRT の基盤にもなる（オーバーエンジニアリング枠） |
| **バンドルサイズ予算** | `vite build` 出力 | size-limit で initial JS / chunk 毎の上限をコミット。エージェントが安易に重量依存を足すのを機械的に阻止 |
| デッドコード検査 | 全 src | **knip**（未使用 export・未使用依存・未使用ファイル）。生成的にコードを書くループは死骸を残しやすく、comprehension rot 対策として効く |
| ミューテーションテスト | validators / format / hooks | StrykerJS。カバレッジラチェットの「テストの空洞化」を検出する上位ゲート（nightly 配置） |
| tsconfig 強化 | tsconfig.app.json | `noUncheckedIndexedAccess` / `exactOptionalPropertyTypes` / `noImplicitOverride` を有効化 |
| Biome 強化 | biome.json | `noExplicitAny` を error へ昇格、lint 対象に `tests/` `scripts/` を追加、a11y ルール群を on |
| E2E 拡充 | sales-case / reservation / consignment | 現在 E2E はロット生涯 + CSV のみ。3 案件種別の主要フロー追加。webkit/firefox は nightly のみで追加 |
| Lighthouse CI | 主要 3 ページ | パフォーマンス予算（LCP/TBT/score 閾値）。nightly 配置（オーバーエンジニアリング枠） |

## 8. CI 基盤そのもの（ゲートを検証するゲート）

ループの信頼性はゲート実装の正しさに依存するが、**ゲートのコード自体が現在まったく無検証**。ここはエージェント自動化リポジトリ特有の急所。

| 種別 | 対象 | 内容 |
|---|---|---|
| shellcheck + shfmt | `scripts/verify.sh`, `apps/api-fsharp/ci.sh`, ralph-orchestrator の `lib/*.sh`（bash 資産が非常に大きい） | verify に追加。fail-closed ロジックのバグ = 全ゲートの fail-open |
| **verify.sh スコープ判定の単体テスト** | `classify_paths` | bats-core でパス分類のテーブルテスト。「未知パス→全検証」の要をリグレッションから守る |
| pytest + ruff | `.claude/scripts/*.py`, `apps/api-fsharp/scripts/*.py`（sarif-merge / junit-to-sarif / sarif-to-lessons / prioritize-from-trivy） | SARIF 集約と LESSONS 学習ループの中枢が無テスト。`sarif-merge.py` のバグは「error レベル検出の握り潰し」に直結する |
| coverage-ratchet.mjs のテスト | `apps/frontend/scripts/` | ラチェット比較ロジック（EPSILON、baseline 更新）の単体テスト |
| actionlint + zizmor | `.github/workflows/*.yml` | 構文・式・セキュリティ（injection、過剰 permissions）の静的検査 |
| renovate-config-validator | renovate.json | nightly の `prioritize-from-trivy.py` が機械的に書き換えるファイルなので、書き換え結果の妥当性検証は必須 |
| ast-grep ルール整備 | リポジトリ全体 | AGENTS.md は「静的検査可能なルールは linter か ast-grep に書く」と宣言しているが **ast-grep ルールは現在ゼロ**。候補: `DateTime.Now` 禁止(§4)、Api 層での SQL 禁止、テストでの `Task.Delay`/sleep 禁止、`console.log` 禁止、テストの `Support/*` 迂回検出 |
| markdownlint + lychee | docs/ / AGENTS.md / dsl/ | ドキュメントが AI ループの入力（SSoT）である以上、リンク切れ・構造崩れは文脈汚染。軽量に nightly へ |
| root .editorconfig | リポジトリ全体 | 現在 F# 用のみ。TS/YAML/MD の基本統一 |

## 9. 決定性そのものを守るゲート（フレーク対策）

自動マージの前提は「赤 = 本当に壊れている」。フレークはループにとって毒。

- **フレーク検出 job（nightly）**: 変更なしで `dotnet test` / `vitest run` を 3 回連続実行し、結果が揺れたら Issue 起票。retry せず「揺れ自体を失敗」とするのが決定的ゲートの立場
- **FsCheck seed 運用の固定化**（docs F6）: 失敗時に replay seed をログへ必ず出力し、CI 環境変数で再現実行できる仕組み。Schemathesis の seed 42 と同じポリシーを PBT にも
- **テスト実行時間ラチェット**: スイート合計時間の baseline 比較（例: +30% で警告→error）。ループは verify を毎ターン回すため、ゲートの速度劣化は自動化のスループット劣化に直結する
- **`onUnhandledRequest: "error"`（実装済）と同型の fail-closed を E2E にも**: Playwright で未スタブの外部リクエストを禁止

## 10. セキュリティ / サプライチェーン（nightly 拡充）

- **SBOM のフロントエンド版**: 現在 CycloneDX は .NET のみ。`cdxgen` 等で pnpm 側も生成し `sbom-frontend.cdx.json` を追加
- **Actions の SHA ピン留め検査**: サードパーティ action をタグでなく commit SHA 固定にし、zizmor/pinact でゲート
- **Trivy config/secret スキャン拡張**: 現在 fs のみ。docker-compose / Dockerfile の misconfiguration スキャンを追加
- **ライセンス検査**: `dotnet-project-licenses` / `license-checker-rseidelsohn` で許可リスト外ライセンスの混入を error（オーバーエンジニアリング枠）
- **osv-scanner**: lockfile ベースの脆弱性照合（Trivy と重複するが検出源が異なる）

## 11. パフォーマンス（完全にオーバーエンジニアリング枠 / nightly）

- k6 or NBomber による主要 5 エンドポイントのスモーク負荷 + p95 レイテンシ閾値ゲート
- BenchmarkDotNet で domain workflow 関数のアロケーション/時間の回帰検出
- 大量データ投入後の `EXPLAIN (FORMAT JSON)` で主要クエリの seq scan 検出ゲート
- フロント: §7 の bundle-size / Lighthouse

---

## 12. 優先度マトリクス

**判断軸**: (a) fail-open の穴か、(b) マージゲートに置けるほど速く決定的か、(c) エージェント自動開発の安全性への寄与。

### Tier 1 — 直ちに（穴塞ぎ・既存資産の昇格。数日規模）
1. Pact provider 検証の Broker レス化 + verify 組込み（1-1）
2. backend カバレッジラチェット（1-2）
3. `Directory.Build.props` で TreatWarningsAsErrors（1-3）
4. アーキテクチャテスト・FSharpLint・gitleaks を verify へ昇格（§2）
5. 統合テスト全レスポンスの openapi スキーマ検証ハンドラ（§5）
6. oasdiff 破壊的変更ゲート（§6）
7. MSW ↔ zod スキーマドリフト検査（§7）
8. `pnpm build` を verify に追加（1-5）
9. shellcheck + actionlint + verify.sh の bats テスト（§8）

### Tier 2 — 次の反復（空白の充填。1–2 週規模）
10. ステートフル PBT の SalesCase/Reservation/Consignment 展開（§3）
11. fast-check PBT Phase 9 実装（§7、設計済み）
12. 認可マトリクス + 契約カバレッジラチェット（§5）
13. スキーマスナップショット + マイグレーション冪等性/順序テスト（§4）
14. SmartConstructors 境界値テスト・Repository round-trip（§3, §4）
15. Schemathesis error 昇格 + examples 検証（1-4, §6）
16. knip / jest-axe / PR スモーク E2E（§7, 1-6）
17. CI スクリプト群（python/mjs）の単体テスト（§8）
18. ast-grep ルール整備（§8）

### Tier 3 — オーバーエンジニアリング容認枠（余力があれば）
19. ミューテーションテスト（StrykerJS。F# は Stryker.NET 非対応のためカバレッジラチェット+PBT で代替）
20. ビジュアルリグレッション / Storybook / Lighthouse（§7）
21. バンドルサイズ予算 / テスト時間ラチェット / フレーク検出 job（§7, §9）
22. LocalStack 統合テスト / Outbox at-least-once property（§4）
23. SBOM フロント版 / ライセンス検査 / SHA ピン留め / osv-scanner（§10)
24. k6 / BenchmarkDotNet / EXPLAIN ゲート（§11）
25. markdownlint / lychee / root .editorconfig（§8）

### 採用しない・保留を推奨するもの
- **Stryker.NET（F#）**: 公式サポート外。無理に導入せず、backend はカバレッジラチェット + PBT + 将来の mutation 相当（DSL 変更→全 match 赤）で代替
- **DSL の専用パーサー/形式検証（Alloy/TLA+）**: `dsl/README.md` が明示的に非採用と宣言済み。§3 の grep レベル整合スクリプトで十分
- **Playwright の retry 導入**: フレーク隠蔽になるため §9 の方針（揺れ=失敗）と矛盾。導入しない
