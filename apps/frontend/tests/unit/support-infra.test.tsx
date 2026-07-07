/**
 * Phase 1 のテスト基盤スモーク — component / page テストが
 * `tests/support/*` を本格利用する前に、基盤が端から端まで動くことを
 * 確認する。
 *
 * 検査範囲:
 *   - MSW が `fetch` を介入し、handler の応答が `apiGet` 内の zod
 *     検証まで往復する
 *   - request capture が URL / method / body を記録する
 *   - `resetHandlers` と `resetCapturedRequests` がケース間で隔離する
 *   - `deferred<T>()` で loading 状態を観測できる
 *   - `renderWithApp` が provider (SWR + Toaster) を例外なく mount する
 *   - `tests/setup.ts` のグローバル stub (`URL.createObjectURL` / anchor
 *     click) が CSV ダウンロードテストで使える状態にある
 */
import { schemas } from "@/contracts";
import { apiDownload, apiGet, apiSend } from "@/lib/api-client";
import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import { deferred } from "../support/deferred";
import { makeCodeMasters, makeLot, makePriceQuote } from "../support/fixtures";
import { renderWithApp } from "../support/render";
import { capturedRequests, requestCount, requestsFor, server } from "../support/server";

describe("tests/support 基盤", () => {
  it("MSW が fetch を介入し、応答を zod でパースできる", async () => {
    server.use(
      http.get("/api/lots/L-0001", () => HttpResponse.json(makeLot({ lotNumber: "L-0001" }))),
    );
    const lot = await apiGet("/lots/L-0001", schemas.LotResponse);
    expect(lot.lotNumber).toBe("L-0001");
    expect(lot.status).toBe("manufacturing");
  });

  it("リクエストの URL / method / body を捕捉する", async () => {
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
    expect(calls[0]!.method).toBe("POST");
    expect(calls[0]!.body).toEqual({ date: "2026-05-01", version: 1 });
    expect(requestCount("/api/lots/L-1/complete-manufacturing")).toBe(1);
  });

  it("handler と捕捉済みリクエストはテスト間でリセットされる", () => {
    // リセットが効いていなければ、前テストの handler が残っているか
    // `capturedRequests` に前ケースの記録が残っているはず。
    expect(capturedRequests).toHaveLength(0);
  });

  it("deferred<T> は自動 settle せず、resolve / reject を外から制御できる", async () => {
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

  it("fixture は contract スキーマを満たす形状を返す", () => {
    expect(() => schemas.LotResponse.parse(makeLot())).not.toThrow();
    expect(makePriceQuote().basePrice).toBe(1000);
    expect(makeCodeMasters().divisions).toHaveLength(2);
  });

  it("renderWithApp は SWR + Toaster を例外なく mount する", () => {
    renderWithApp(<div data-testid="probe">ok</div>);
    expect(screen.getByTestId("probe")).toHaveTextContent("ok");
  });

  it("URL.createObjectURL と anchor click が blob ダウンロード用に stub されている", async () => {
    server.use(http.get("/api/lots/lot-1.csv", () => HttpResponse.text("a,b\n1,2\n")));
    const clickSpy = vi.spyOn(HTMLAnchorElement.prototype, "click");
    await apiDownload("/lots/lot-1.csv", "lot-1.csv");
    expect(URL.createObjectURL).toHaveBeenCalledOnce();
    expect(URL.revokeObjectURL).toHaveBeenCalledWith("blob:mock");
    expect(clickSpy).toHaveBeenCalledOnce();
  });
});
