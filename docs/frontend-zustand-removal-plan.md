# frontend zustand 脱却計画

## 背景

`apps/frontend` は現在 `zustand` を直接依存として持ち、`src/stores/auth-store.ts` と
`src/stores/ui-store.ts` で利用している。

調査時点の利用状況は以下のとおり。

- `auth-store.ts`
  - JWT、ロール、ユーザー ID、`setToken`、`clear`、`hasRole` を保持している
  - `api-client.ts` が React コンポーネント外から現在の token を同期的に参照している
  - `Guard.tsx` と `RoleBadge.tsx` が React コンポーネント内で購読している
- `ui-store.ts`
  - ロット画面向けのモーダル状態とフィルタ状態を定義している
  - `useUi` はファイル外から参照されておらず、現状は未使用

また、`@tanstack/react-query` は現在の依存には含まれていない。サーバー状態の取得と
キャッシュは既存どおり `SWR` が担っている。導入済みの TanStack 系依存は
`@tanstack/react-router` であり、データ取得キャッシュ用途ではない。

## 方針

`zustand` は技術的に必須ではないため、依存から外す。

責務は以下のように分離する。

- サーバー状態: 既存の `SWR` を継続利用する
- クライアント認証状態: `useSyncExternalStore` を使った小さな外部 store に置き換える
- 未使用 UI 状態: `ui-store.ts` を削除する

`auth-store` は React コンポーネント外から同期的に読まれるため、React Context だけには寄せない。
`useSyncExternalStore` ベースの store にすることで、以下の要件を両立する。

- `api-client.ts` から `authStore.getSnapshot()` で token を同期的に参照できる
- `Guard.tsx` / `RoleBadge.tsx` から `useAuth(selector)` で購読できる
- React 標準 API だけで実装でき、追加の状態管理依存を持たない

## 完了条件

- `apps/frontend/package.json` から `zustand` が削除されている
- `apps/frontend/pnpm-lock.yaml` から `zustand` の direct dependency が削除されている
- `apps/frontend/src/stores/ui-store.ts` が削除されている
- `apps/frontend/src/stores/auth-store.ts` が `zustand` を import していない
- `apps/frontend/src` と `apps/frontend/tests` に `zustand` への参照が残っていない
- 既存の認証挙動が維持されている
  - JWT から `realm_access.roles` と `sub` を復元できる
  - 不正な JWT で例外を投げず、ロールなしとして扱う
  - `viewer` < `operator` < `admin` の階層判定が維持される
  - API リクエストに token がある場合は `Authorization: Bearer ...` を付与する
  - 401 応答時に認証状態を clear する
  - `<Guard>` と `<RoleBadge>` が認証状態の変更に追従する
- `pnpm test`、`pnpm typecheck`、`pnpm lint` が通る

## TDD 計画

### 1. 探索

対象ファイルと参照関係を確定する。

- `apps/frontend/src/stores/auth-store.ts`
- `apps/frontend/src/stores/ui-store.ts`
- `apps/frontend/src/lib/api-client.ts`
- `apps/frontend/src/components/auth/Guard.tsx`
- `apps/frontend/src/components/layout/RoleBadge.tsx`
- `apps/frontend/tests/unit/auth-store.test.ts`
- `apps/frontend/tests/unit/api-client.test.ts`
- `apps/frontend/tests/unit/backend-contract.test.tsx`

確認コマンド:

```bash
rg -n "zustand|useAuth|getState|useUi|auth-store|ui-store" apps/frontend/src apps/frontend/tests apps/frontend/package.json
```

### 2. Red

まずテストを新しい API 前提に変更し、現行実装で失敗する状態を作る。

`auth-store` のテストは `useAuth.getState()` 前提をやめ、明示的な store API を使う。

想定 API:

```ts
authStore.getSnapshot()
authStore.setToken(token)
authStore.clear()
useAuth(selector)
```

追加または変更する観点:

- `authStore.getSnapshot()` が初期状態を返す
- `authStore.setToken()` が JWT を decode して snapshot を更新する
- `authStore.clear()` が token、roles、userId を初期化する
- `useAuth(selector)` が store 更新時に再 render される
- `src/` と `tests/` に `from "zustand"` が残っていないことを検査する

### 3. Green

`auth-store.ts` を `useSyncExternalStore` ベースに置き換える。

実装イメージ:

```ts
type AuthSnapshot = {
  token: string | null;
  roles: ReadonlySet<string>;
  userId: string | null;
  hasRole: (role: Role) => boolean;
};

export const authStore = {
  getSnapshot,
  subscribe,
  setToken,
  clear,
};

export function useAuth<T>(selector: (snapshot: AuthSnapshot) => T): T {
  return useSyncExternalStore(
    authStore.subscribe,
    () => selector(authStore.getSnapshot()),
    () => selector(authStore.getSnapshot()),
  );
}
```

実装時の注意:

- `getSnapshot()` 内で毎回オブジェクトを作らない
- token 更新または clear のタイミングでのみ snapshot を差し替える
- `roles` は `ReadonlySet<string>` として公開し、外部からの破壊的変更を避ける
- `hasRole` は現在の snapshot 内の roles を参照する
- 不正な JWT は例外にせず、roles 空、userId `null` として扱う

### 4. 呼び出し側の更新

`api-client.ts` は React hook を使わず、store API を直接使う。

- `useAuth.getState().token` を `authStore.getSnapshot().token` に変更する
- `useAuth.getState().clear()` を `authStore.clear()` に変更する

React コンポーネントは既存に近い利用形を維持する。

- `Guard.tsx`: `useAuth((s) => s.hasRole(requiredRole))`
- `RoleBadge.tsx`: `useAuth((s) => s.roles)` と `useAuth((s) => s.token)`

テストのセットアップや検証も `authStore` に寄せる。

### 5. 未使用 UI store の削除

`ui-store.ts` は未使用のため削除する。

削除前後に以下を確認する。

```bash
rg -n "useUi|LotModalKind|LotStatusFilter|ui-store" apps/frontend/src apps/frontend/tests
```

ヒットが `ui-store.ts` 自身だけであることを確認したうえで削除する。

### 6. 依存削除

`zustand` を direct dependency から削除し、lockfile を更新する。

候補コマンド:

```bash
pnpm remove zustand
```

または、必要に応じて以下を使う。

```bash
pnpm install --lockfile-only
```

### 7. 検証

`apps/frontend` で以下を実行する。

```bash
pnpm test
pnpm typecheck
pnpm lint
rg -n "zustand" src tests package.json
```

余力があれば build も確認する。

```bash
pnpm build
```

## リスクと対策

### selector の戻り値が毎回変わる問題

`useSyncExternalStore` の snapshot は、状態が変わっていないとき同じ参照を返す必要がある。
`getSnapshot()` 内で新しい object や `Set` を作ると不要な再 render や警告の原因になる。

対策として、snapshot は module scope に保持し、`setToken` と `clear` のときだけ差し替える。

### `ReadonlySet` の実行時破壊

TypeScript の `ReadonlySet` は型上の制約であり、実行時の破壊を完全には防がない。

現状の利用は `has` と `size` の読み取りだけなので実害は小さい。必要になったら roles を
`readonly string[]` に変えるか、`hasRole` と `roleNames` のみ公開する。

### 将来の OIDC 導入

README では将来 `oidc-client-ts` を導入し、OIDC ライフサイクルから `setToken()` を呼ぶ構想がある。
`authStore.setToken()` を明示 API として残せば、この構想は維持できる。

### React Query への移行と混同しない

今回の目的は `zustand` 脱却であり、SWR から React Query への移行は別タスクに分ける。
認証 token の現在値はサーバーキャッシュではなくクライアントセッション状態なので、
React Query に載せるより外部 store に置く方が責務が明確になる。

## 推奨実施順

1. `auth-store` のテストを新 API に更新する
2. `auth-store` を `useSyncExternalStore` で再実装する
3. `api-client` とテストを `authStore` API に移行する
4. `Guard` / `RoleBadge` の挙動テストを確認する
5. `ui-store.ts` を削除する
6. `zustand` 依存を削除する
7. テスト、型検査、lint を実行する
