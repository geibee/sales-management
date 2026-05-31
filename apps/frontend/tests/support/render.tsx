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
import {
  Outlet,
  RouterProvider,
  createMemoryHistory,
  createRootRoute,
  createRoute,
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
 * 本物のルートツリーが要るテスト (Phase 6 など) は `routeTree` を
 * import して自前の initialPath で起動する想定 (現時点では未使用)。
 */
export function renderWithRouter(
  ui: ReactElement,
  { initialPath = "/" }: RenderWithRouterOptions = {},
): RenderResult {
  const rootRoute = createRootRoute({ component: () => <Outlet /> });
  const catchAll = createRoute({
    getParentRoute: () => rootRoute,
    path: "$",
    component: () => ui,
  });
  const router = createRouter({
    routeTree: rootRoute.addChildren([catchAll]),
    history: createMemoryHistory({ initialEntries: [initialPath] }),
  });
  return render(
    <AppProviders>
      <RouterProvider router={router as never} />
    </AppProviders>,
  );
}
