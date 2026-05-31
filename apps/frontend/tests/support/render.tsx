/**
 * Render helpers used by component/page tests.
 *
 * `renderWithApp` — wraps `ui` in the providers a real page expects
 * (SWRConfig with caching disabled + `<Toaster>` so `toast.*` is a
 * no-op rather than throwing). It does NOT mount the router; pages
 * that need routing should use `renderWithRouter`.
 *
 * `renderWithRouter` — boots a real TanStack Router with an in-memory
 * history starting at `initialPath`. Use this when the test exercises
 * navigation (route guards, `navigate({to})` after a mutation, etc.).
 *
 * Both helpers reset the auth store before mounting so tests don't
 * leak tokens between cases.
 */
import { Toaster } from "@/components/ui/sonner";
import { useAuth } from "@/stores/auth-store";
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
        // Avoid cross-test contamination via SWR's module-level cache.
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
  useAuth.getState().clear();
  return render(<AppProviders>{ui}</AppProviders>);
}

export interface RenderWithRouterOptions {
  initialPath?: string;
}

/**
 * Render `ui` inside a real TanStack Router. The router is built ad
 * hoc with a single catch-all route that renders `ui`, so callers
 * don't depend on the production `routeTree.gen.ts`.
 *
 * Tests that need the real route tree should import `routeTree` and
 * pass their own initial path; that's deferred to whichever Phase 6
 * (router integration) test needs it.
 */
export function renderWithRouter(
  ui: ReactElement,
  { initialPath = "/" }: RenderWithRouterOptions = {},
): RenderResult {
  useAuth.getState().clear();
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
