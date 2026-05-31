import { LotResponseSchema } from "@/contracts";
/**
 * Phase 1 infrastructure smoke — proves `tests/support/*` works end
 * to end before component/page tests start consuming it.
 *
 * Covers:
 *   - MSW intercepts a `fetch` request and the handler reply round-trips
 *     through zod validation in `apiGet`
 *   - request capture records the URL/method/body
 *   - `resetHandlers` + `resetCapturedRequests` isolate cases
 *   - `deferred<T>()` lets a test observe loading state
 *   - `renderWithApp` mounts providers (SWR + Toaster) without throwing
 *   - the global `URL.createObjectURL` / anchor-click stubs from
 *     `tests/setup.ts` are in place for CSV download tests
 */
import { apiDownload, apiGet, apiSend } from "@/lib/api-client";
import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import { deferred } from "../support/deferred";
import { makeCodeMasters, makeLot, makePriceQuote } from "../support/fixtures";
import { renderWithApp } from "../support/render";
import { capturedRequests, requestCount, requestsFor, server } from "../support/server";

describe("tests/support infrastructure", () => {
  it("MSW intercepts fetch and zod-parses the response", async () => {
    server.use(
      http.get("/api/lots/L-0001", () => HttpResponse.json(makeLot({ lotNumber: "L-0001" }))),
    );
    const lot = await apiGet("/lots/L-0001", LotResponseSchema);
    expect(lot.lotNumber).toBe("L-0001");
    expect(lot.status).toBe("manufacturing");
  });

  it("captures request URL / method / body", async () => {
    server.use(
      http.post(
        "/api/lots/L-1/complete-manufacturing",
        () => new HttpResponse(null, { status: 204 }),
      ),
    );
    await apiSend("POST", "/lots/L-1/complete-manufacturing", {
      date: "2026-05-01",
      version: 1,
    });
    const calls = requestsFor("/api/lots/L-1/complete-manufacturing");
    expect(calls).toHaveLength(1);
    expect(calls[0].method).toBe("POST");
    expect(calls[0].body).toEqual({ date: "2026-05-01", version: 1 });
    expect(requestCount("/api/lots/L-1/complete-manufacturing")).toBe(1);
  });

  it("resets handlers and capture between tests", () => {
    // If reset didn't work, the previous test's handler would still be
    // registered and `capturedRequests` would already contain entries.
    expect(capturedRequests).toHaveLength(0);
  });

  it("deferred<T> exposes resolve/reject without auto-settling", async () => {
    const d = deferred<number>();
    let resolved: number | null = null;
    d.promise.then((v) => {
      resolved = v;
    });
    await Promise.resolve();
    expect(resolved).toBeNull();
    d.resolve(42);
    await d.promise;
    expect(resolved).toBe(42);
  });

  it("fixtures produce shapes that satisfy the contract schemas", () => {
    expect(() => LotResponseSchema.parse(makeLot())).not.toThrow();
    expect(makePriceQuote().basePrice).toBe(1000);
    expect(makeCodeMasters().divisions).toHaveLength(2);
  });

  it("renderWithApp mounts SWR + Toaster without throwing", () => {
    renderWithApp(<div data-testid="probe">ok</div>);
    expect(screen.getByTestId("probe")).toHaveTextContent("ok");
  });

  it("URL.createObjectURL + anchor click are stubbed for blob downloads", async () => {
    server.use(http.get("/api/lots/lot-1.csv", () => HttpResponse.text("a,b\n1,2\n")));
    const clickSpy = vi.spyOn(HTMLAnchorElement.prototype, "click");
    await apiDownload("/lots/lot-1.csv", "lot-1.csv");
    expect(URL.createObjectURL).toHaveBeenCalledOnce();
    expect(URL.revokeObjectURL).toHaveBeenCalledWith("blob:mock");
    expect(clickSpy).toHaveBeenCalledOnce();
  });
});
