/**
 * MSW node server for unit/component tests.
 *
 * Two responsibilities:
 *   1. `server` — the `setupServer()` instance. Lifecycle (listen /
 *      resetHandlers / close) is wired in `tests/setup.ts`.
 *   2. `capturedRequests` — every intercepted request is appended so
 *      tests can assert URL / method / body / headers without re-mocking
 *      `fetch`. Reset between tests by `tests/setup.ts`.
 *
 * `BASE` mirrors `src/lib/api-client.ts` so handlers can be authored
 * either as full `${BASE}/path` or as relative `/api/path` strings.
 */
import { setupServer } from "msw/node";

export const BASE = "/api";

export interface CapturedRequest {
  method: string;
  url: string;
  pathname: string;
  search: string;
  headers: Record<string, string>;
  body: unknown;
}

export const capturedRequests: CapturedRequest[] = [];

export const server = setupServer();

server.events.on("request:start", async ({ request }) => {
  // Clone before reading the body — the original is consumed by the
  // resolver downstream.
  const cloned = request.clone();
  let body: unknown = null;
  const ct = cloned.headers.get("content-type") ?? "";
  if (ct.includes("application/json")) {
    try {
      body = await cloned.json();
    } catch {
      body = null;
    }
  } else if (request.method !== "GET" && request.method !== "HEAD") {
    try {
      body = await cloned.text();
    } catch {
      body = null;
    }
  }
  const url = new URL(request.url);
  const headers: Record<string, string> = {};
  request.headers.forEach((v, k) => {
    headers[k] = v;
  });
  capturedRequests.push({
    method: request.method,
    url: request.url,
    pathname: url.pathname,
    search: url.search,
    headers,
    body,
  });
});

export function resetCapturedRequests(): void {
  capturedRequests.length = 0;
}

/** Filter captured requests by pathname (exact match). */
export function requestsFor(pathname: string): CapturedRequest[] {
  return capturedRequests.filter((r) => r.pathname === pathname);
}

/** Count captured requests by pathname (exact match). */
export function requestCount(pathname: string): number {
  return requestsFor(pathname).length;
}
