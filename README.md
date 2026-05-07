# sales-management

[仕様駆動開発](https://github.com/geibee/sdd-tutorial)のF#版実装のリポジトリ。  
販売管理ドメインの PoC リポジトリ。F# (Giraffe) によるバックエンド API と React フロントエンドをモノレポで管理し、マルチエージェントオーケストレーション (`.harness/`) と RALPH ループ (`harness/ralph.sh`) による増分開発を前提とする。

## 構成

```
sales-management/
├── apps/
│   ├── api-fsharp/        # F# (Giraffe + Donald) によるバックエンド
│   └── frontend/          # React 19 + Vite + TanStack Router フロントエンド
├── dsl/                   # DSL (SSoT)
│   ├── domain-model.md    # ドメインモデル DSL 本体
│   └── grammar.ebnf       # DSL の形式文法 ([CORE] / [VERIFICATION] 層に区分)
├── harness/               # RALPH ループ + 意味論ガイド
│   ├── SEMANTICS.md       # DSL → 各ターゲット言語の翻訳規則
│   ├── reference/         # 言語別リファレンス実装（ゴールド標準）
│   └── ralph.sh           # green-loop runner
├── tools/
│   └── dsl-parser/        # DSL → AST パーサー (Python / uv)
├── pacts/                 # Pact によるコントラクトテスト成果物
├── .harness/              # マルチエージェントオーケストレーション基盤
├── .claude/               # Claude Code 用フック / 設定
├── docker-compose.harness.yml  # Pact Broker / Jaeger 起動用
├── prd.md                 # PRD (RALPH ループが消化するタスクリスト)
├── progress.txt           # RALPH 反復ログ
└── AGENTS.md              # 開発スタイル / コーディング規約 / 運用ルール
```

## 主要技術スタック

### バックエンド (`apps/api-fsharp/`)
- F# / .NET
- Giraffe (HTTP)
- Donald (DB アクセス)
- OpenAPI (`openapi.yaml`) を SSoT としたコントラクト駆動

### フロントエンド (`apps/frontend/`)
- React 19 + TypeScript
- Vite 6
- TanStack Router
- Tailwind CSS v4 / Radix UI / shadcn 系コンポーネント
- Zod / Zodios (OpenAPI から型生成)
- React Hook Form / SWR / Zustand
- テスト: Vitest (ユニット) / Playwright (E2E)
- Lint / Format: Biome

## セットアップ

### 前提
- Node.js >= 24, pnpm
- .NET SDK (F# 対応版)
- Docker / Docker Compose (Pact Broker, Jaeger を起動する場合)

### フロントエンド

```bash
cd apps/frontend
pnpm install
pnpm dev          # 開発サーバ起動
pnpm typecheck    # 型チェック
pnpm lint         # Biome
pnpm test         # Vitest
pnpm test:e2e     # Playwright
pnpm gen:api      # OpenAPI から Zodios クライアントを再生成
pnpm build        # 本番ビルド
```

### バックエンド

```bash
cd apps/api-fsharp
dotnet tool restore
dotnet build
dotnet test
bash ci.sh        # CI 一式（SARIF を ci-results/ に出力）
```

### DSL パーサー (`tools/dsl-parser/`)

`dsl/domain-model.md` を AST に変換するツール。SDD で各派生物（F# 型・Alloy・TLA+ 等）を生成する際の入力。

```bash
cd tools/dsl-parser
uv sync
uv run pytest          # ゴールデンテスト + ドメインモデル全体のスモークテスト
uv run dsl-parser ../../dsl/domain-model.md   # AST を JSON で出力
bash ci.sh             # CI 一式
```

### 補助サービス (Pact Broker / Jaeger)

```bash
docker compose -f docker-compose.harness.yml up -d
# Pact Broker: http://localhost:9292 (basic auth: pact / pact)
# Jaeger UI:   http://localhost:16686
```

## ローカル起動の流れ

API + フロントを実際に動かす手順。各コマンドはリポジトリルートからの相対パスで記載。

### 1. DB / 周辺サービスを起動

`apps/api-fsharp/docker-compose.yml` に Postgres・WireMock・LocalStack・Jaeger 等が定義されている。最低限 DB のみ起動すれば API は動く。

```bash
cd apps/api-fsharp
docker compose up -d db          # Postgres :5432 (app / app / sales_management)
# 必要に応じて: docker compose up -d wiremock localstack jaeger
```

### 2. マイグレーション

`tools/Migrator` が `apps/api-fsharp/migrations/` 配下の SQL を順に適用する（DbUp）。

```bash
cd apps/api-fsharp
dotnet run --project tools/Migrator
# 接続先を変える場合は DATABASE_URL を上書き
# DATABASE_URL='Host=localhost;Port=5432;Database=sales_management;Username=app;Password=app' \
#   dotnet run --project tools/Migrator
```

### 3. API サーバ起動

```bash
cd apps/api-fsharp
dotnet run --project src/SalesManagement
# http://localhost:5000 で待ち受け（appsettings.json の Server.Port）
# 開発環境の認証は appsettings.json で Authentication.Enabled=false
```

### 4. フロントエンド起動

別ターミナルで:

```bash
cd apps/frontend
pnpm install        # 初回のみ
pnpm dev            # http://localhost:5173
# Vite が API リクエストを http://localhost:5000 にプロキシ（vite.config.ts）
```

### 5. （任意）開発用シードデータ投入

API 起動後、公開 API 経由でロット・販売案件を投入する冪等スクリプト。

```bash
bash apps/api-fsharp/scripts/seed-dev-data.sh
# API= で接続先を変更可: API=http://localhost:5000 bash ...
```

投入後の確認用 URL は同スクリプトの末尾出力を参照。

## 開発スタイル

- **TDD**: 探索 → Red → Green → Refactoring
- **言語**: ドキュメント・コミット・コメントは日本語、コード上の識別子は英語
- **設計**: 関心の分離 / 状態とロジックの分離 / コントラクト層 (API・型) を厳密に
- **静的検査**: linter または ast-grep に書く（プロンプトに混ぜない）

詳細・命名規約・DSL 解釈ルールは [`AGENTS.md`](./AGENTS.md) を参照。

## マルチエージェント開発と RALPH ループ

本リポジトリは PRD (`prd.md`) を SSoT としたタスクリストを、専門エージェントに分配して消化する。

### タスクを dry-run（inbox 書き込みのみ）

```bash
python3 .harness/master.py --prd prd.md --dry-run
```

### RALPH ループ（CI が緑になるまで自己反復）

```bash
bash harness/ralph.sh
```

停止条件:
- `prd.md` の全項目が `[x]` になる
- `MAX_ITER`（デフォルト 20）到達
- `BUDGET_USD`（デフォルト 10）到達

エージェント間通信は `.harness/inbox/<agent>/` と `.harness/outbox/<agent>/` の JSON メッセージのみ（直接呼び出しは禁止）。詳細は [`.harness/README.md`](./.harness/README.md) を参照。

## CI 出力

すべての CI ツールは結果を SARIF で `ci-results/sarif/<tool>.sarif` に出力し、`ci-results/merged.sarif` に統合される（`ci-results/` は gitignore 対象）。

```
ci-results/
├── sarif/
│   ├── gitleaks.sarif
│   ├── trivy.sarif
│   ├── detekt.sarif
│   ├── fsharp-build.sarif
│   ├── sonar.sarif
│   └── zap.sarif
├── merged.sarif
├── sbom-fsharp.cdx.json
└── renovate.log
```

失敗の自己分析は Stop フック (`.claude/scripts/sarif-to-lessons.py`) が `AGENTS.md` 末尾の "失敗から学んだこと (自動生成)" セクションに追記する。

## ライセンス

[MIT](./LICENSE)
