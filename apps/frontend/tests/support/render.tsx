/**
 * component / page テストで使う render ヘルパー。
 *
 * `renderWithApp` — 実 page が期待する provider 群 (SWR キャッシュを
 * テストごとに隔離した SWRConfig + sonner の `<Toaster>` で `toast.*`
 * を no-op にする) を巻いて描画する。ルータは載せないので、`<Link>` や
 * `useNavigate` を使う page は `renderWithRouter` を使う。
 *
 * `renderWithRouter` — メモリ history で本物の TanStack Router を
 * `initialPath` から起動する。ルーティング (ガード、mutation 成功後の
 * `navigate({to})` など) を試したいテスト向け。
 *
 * `useAuth` (zustand) はモジュールレベル状態のため、各テストは
 * `beforeEach` で `useAuth.getState().clear()` するのが前提。
 * render ヘルパー側では auth-store に触れないので、render 前に
 * `setToken(...)` した内容はそのまま残る。
 */
import { Toaster } from "@/components/atoms/sonner";
import { routeTree } from "@/routeTree.gen";
import {
  RouterProvider,
  createMemoryHistory,
  createRootRoute,
  createRouter,
} from "@tanstack/react-router";
import { type RenderResult, render } from "@testing-library/react";
import type { ReactElement, ReactNode } from "react";
import { SWRConfig } from "swr";

function AppProviders({ children }: { children: ReactNode }) {
  return (
    <SWRConfig
      value={{
        // SWR のモジュールレベルキャッシュ経由でテスト間に値が漏れないよう、テストごとに新しい Map を割り当てる。
        provider: () => new Map(),
        revalidateOnFocus: false,
        dedupingInterval: 0,
        shouldRetryOnError: false,
      }}
    >
      {children}
      <Toaster position="top-right" />
    </SWRConfig>
  );
}

export function renderWithApp(ui: ReactElement): RenderResult {
  return render(<AppProviders>{ui}</AppProviders>);
}

export interface RenderWithRouterOptions {
  initialPath?: string;
}

/**
 * 即席で組んだ catch-all ルート 1 本のうえに `ui` を載せて TanStack
 * Router を立ち上げる。production の `routeTree.gen.ts` に依存しない。
 *
 * 本物のルートツリーが要るテスト (Phase 6 の FE-NAV-*) は
 * `renderWithRealRouter` を使う。
 */
export function renderWithRouter(
  ui: ReactElement,
  { initialPath = "/" }: RenderWithRouterOptions = {},
): RenderResult {
  // root route 自身を `ui` のレンダリング先にする。catch-all や index
  // route を組み立てるよりも、initialPath が "/" でも確実に発火する。
  const rootRoute = createRootRoute({ component: () => ui });
  const router = createRouter({
    routeTree: rootRoute,
    history: createMemoryHistory({ initialEntries: [initialPath] }),
  });
  return render(
    <AppProviders>
      <RouterProvider router={router as never} />
    </AppProviders>,
  );
}

/**
 * production の `routeTree.gen.ts` をそのまま使い、メモリ history で
 * `initialPath` から起動する (Phase 6 / FE-NAV-*)。route 解決・
 * `navigate({to})` の実遷移・実 route 上の Guard fallback を検査できる。
 * root route が `Shell` を描画するため、Sidebar (/lots・/sales-cases) と
 * Topbar (/health) の MSW handler が必要になる点に注意。
 */
export function renderWithRealRouter(initialPath: string): RenderResult {
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: [initialPath] }),
  });
  return render(
    <AppProviders>
      <RouterProvider router={router as never} />
    </AppProviders>,
  );
}
