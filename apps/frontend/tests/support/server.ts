/**
 * unit / component テスト用の MSW node server。
 *
 * 主な役割は 2 つ:
 *   1. `server` — `setupServer()` インスタンス。lifecycle (listen /
 *      resetHandlers / close) は `tests/setup.ts` で配線する。
 *   2. `capturedRequests` — 介入したリクエストを順次追記するので、
 *      テスト側は `fetch` を再モックせずに URL / method / body /
 *      headers をアサートできる。各テスト後に `tests/setup.ts` から
 *      リセットされる。
 *
 * `BASE` は `src/lib/api-client.ts` と同じ値にしてあるので、handler は
 * フル URL (`${BASE}/path`) でも相対 (`/api/path`) でも書ける。
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
  // body を読む前に clone する — オリジナルは下流の resolver で消費される。
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

/** pathname の完全一致で捕捉済みリクエストを抽出する。 */
export function requestsFor(pathname: string): CapturedRequest[] {
  return capturedRequests.filter((r) => r.pathname === pathname);
}

/** pathname の完全一致で捕捉済みリクエストの件数を返す。 */
export function requestCount(pathname: string): number {
  return requestsFor(pathname).length;
}
