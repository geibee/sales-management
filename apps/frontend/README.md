# Sales Management Admin (Frontend)

React 19 + Vite + TypeScript で書かれた、F# Sales Management API (`../api-fsharp/`) の管理画面 SPA。

## スタック

| 領域 | 採用 |
|---|---|
| ビルド | Vite 6 + `@vitejs/plugin-react` |
| 言語 | TypeScript 5 + React 19 |
| ルーティング | TanStack Router (file-based, 100% type-safe) |
| サーバー状態 | SWR |
| クライアント状態 | Zustand (`auth-store`, `ui-store`) |
| バリデーション | Zod (`openapi-zod-client` で OpenAPI から自動生成 + 手書き拡張) |
| スタイル | Tailwind CSS v4 + shadcn/ui (new-york) |
| フォーム | React 19 native (`<form action>` + `useActionState`) |
| JWT | `jose` (decode-only。署名検証はサーバ側) |
| Lint/Format | Biome |
| ユニットテスト | Vitest + Testing Library + jsdom |
| E2E | Playwright |

## 起動手順

### 1. 依存インストール

```bash
pnpm install
```

### 2. バックエンド起動 (別ターミナル)

```bash
cd ../api-fsharp
docker compose up -d                      # PostgreSQL 起動
dotnet run --project src/SalesManagement   # http://localhost:5000
```

`appsettings.json` の `Authentication.Enabled` で認証 ON/OFF を切替。Phase 1 開発時は OFF を推奨。

### 3. フロントエンド起動

```bash
cp .env.example .env.local        # 必要に応じて編集
pnpm dev                          # http://localhost:5173
```

Vite の `server.proxy` で `/api` → `http://localhost:5000` に転送するので、`VITE_API_BASE_URL=/api` のままで動作します。

## 認証 (Phase 1: 開発者トークン .env 注入)

Phase 1 では OIDC フローを実装せず、開発者が IdP (Keycloak 等) または下記の `DevTokenMint` CLI で発行した JWT を `.env.local` の `VITE_DEV_TOKEN` に手動で貼り付けます:

```bash
# .env.local
VITE_DEV_TOKEN=<paste-your-jwt-here>
```

トークンは `auth-store` 起動時に読まれ、`realm_access.roles` から `viewer` / `operator` / `admin` を抽出します。`<Guard role="operator">` で囲まれた UI はロール階層 (`admin` ⊃ `operator` ⊃ `viewer`) に従って活性/非活性が決まります。

### `DevTokenMint` で開発者トークンを発行する

`Authentication.Enabled=true` で動かしたいときは、リポジトリ同梱の CLI で署名済み JWT を発行できます:

```bash
# admin ロールで 1 時間有効な JWT を出力
dotnet run --project ../api-fsharp/tools/DevTokenMint -- --role admin

# 出力された JWT を .env.local の VITE_DEV_TOKEN に貼り付ける
echo 'VITE_DEV_TOKEN=<上の出力>' >> .env.local
```

詳細・他オプション (`--ttl`, `--audience` 等) は `../api-fsharp/tools/DevTokenMint/README.md` を参照。

### 認証 OFF のバックエンドで mutation を試したい場合

バックエンドが `Authentication.Enabled=false` で動いている場合、フロントエンドは起動時に
`GET /auth/config` を叩き `{ enabled: false }` を受け取ったら自動的にすべての `<Guard>`
を permissive にします。

### Phase 2 (将来)

`oidc-client-ts` を導入して Authorization Code + PKCE フロー、ログイン画面、サイレントリフレッシュを実装予定。`auth-store.setToken()` を OIDC ライフサイクルから呼ぶように差し替えるだけで済む構造にしています。

## ディレクトリ構成

```
frontend/
├── src/
│   ├── routes/                 (TanStack Router file-based)
│   ├── routeTree.gen.ts        (auto-generated, committed)
│   ├── pages/                  (各ルートのページ実体)
│   │   ├── lots/
│   │   ├── sales-cases/
│   │   ├── reservation-cases/
│   │   ├── consignment-cases/
│   │   └── external/
│   ├── hooks/                  (SWR + mutation per aggregate)
│   ├── stores/                 (Zustand: auth-store, ui-store)
│   ├── lib/                    (api-client, utils)
│   ├── contracts/              (Zod: generated + 手書き拡張)
│   ├── components/
│   │   ├── ui/                 (shadcn/ui primitives)
│   │   ├── auth/Guard.tsx
│   │   └── layout/             (Shell, Header, HealthIndicator, RoleBadge, SwaggerLink)
│   └── styles.css              (Tailwind v4 entrypoint + design tokens)
├── tests/
│   ├── unit/                   (Vitest)
│   └── e2e/                    (Playwright)
└── ...
```

## スクリプト

| コマンド | 用途 |
|---|---|
| `pnpm dev` | dev サーバー (http://localhost:5173) |
| `pnpm build` | 本番ビルド (`tsc -b && vite build`) |
| `pnpm preview` | ビルド成果物のプレビュー |
| `pnpm typecheck` | `tsc -b --noEmit` |
| `pnpm lint` | Biome lint |
| `pnpm format` | Biome format --write |
| `pnpm test` | Vitest (ユニット) |
| `pnpm test:e2e` | Playwright (E2E スモーク。`E2E_BACKEND=1` でバックエンド連携テストも有効化) |
| `pnpm gen:api` | OpenAPI → Zod 再生成 (`src/contracts/generated.ts`) |

## 既知の制約 / 留意点

- **OpenAPI と Zod の整合**: `pnpm gen:api` 出力 (`src/contracts/generated.ts`) を一次定義とし、`src/contracts/index.ts` は `ProblemJson` / `SalesCaseDetailResponse`（spec から漏れている GET レスポンス）/ `DateOnly` の3つだけ手書きで補完しています。
- **楽観ロック (version) と 409 Conflict**: ロットの状態遷移は `{ ..., version }` を必須とし、サーバー側は `UPDATE ... WHERE version = @expected` で衝突検出します。フロントは `use-lot.ts` の `withConflictRefresh` で 409 受領時に `globalMutate(lotKey(id))` で再取得 → toast「他の人が更新しました…」を表示します。
- **状態遷移ボタンの活性**: `LotDetailPage` は `lib/format.ts` の `lotActionEnabled` で現在ステータスに応じて 4 アクションを有効/無効化します (`manufacturing` → 製造完了 のみ、など)。
- **状態遷移の請求ボディ**: 価格査定・売買契約・委託結果など複雑なリクエストは Phase 1 では `JsonActionForm` (textarea) で受け付けます。Phase 2 で集約ごとの typed form に差し替えていく前提。
- **外部価格チェック**: `lotId` クエリ必須でバックエンドが 400 を返します。`PriceCheckPage` は `lotId` 入力欄に値が入るまで取得ボタンを disabled。
- **Playwright 実行**: `libnspr4 / libnss3 / libasound2t64` が必要。WSL/Linux で `sudo pnpm exec playwright install-deps` を1回実行してください。

## トラブルシューティング

| 症状 | 対処 |
|---|---|
| `RouterProvider` が未定義 | `pnpm dev` を一度起動して `src/routeTree.gen.ts` を生成 |
| 401 のループ | `.env.local` の `VITE_DEV_TOKEN` が期限切れ。再取得して貼り直す |
| サーキット OPEN (503) | 外部価格 API のサーキットが開いた。バックエンド再起動 or リセット待ち |
| Zod パースエラー | バックエンドのレスポンス形状が想定と乖離。`src/contracts/index.ts` の手書きスキーマを `.passthrough()` に緩める or 仕様を更新 |
