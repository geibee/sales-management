/**
 * `hooks/use-sales-case` のミューテーション関数を直接検証する
 * (issue #9 §7「SWR フックはミューテーション URL・キー・エラーパスを直接検証」)。
 *
 * page テストが通らない経路 (価格査定 更新 / 出荷指示・完了 / 案件削除) の
 * URL・method・body と、409 時にエラーが握り潰されないこと (呼び出し元の
 * toast 分岐が機能する前提) を oracle 化する。
 */
import {
  completeSalesShipping,
  deleteSalesCase,
  instructSalesShipping,
  salesCaseKey,
  updateAppraisal,
} from "@/hooks/use-sales-case";
import { ApiError } from "@/lib/api-client";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { requestsFor, server } from "../../support/server";

const ID = "2026-1-1";

describe("use-sales-case mutations", () => {
  it("salesCaseKey は SWR キー `/sales-cases/{id}` を返す", () => {
    expect(salesCaseKey(ID)).toBe(`/sales-cases/${ID}`);
  });

  it("updateAppraisal → PUT /sales-cases/{id}/appraisals に body をそのまま送る", async () => {
    server.use(
      http.put(`/api/sales-cases/${ID}/appraisals`, () =>
        HttpResponse.json({ salesCaseNumber: ID, status: "appraised", version: 3 }),
      ),
    );
    await updateAppraisal(ID, { taxExcludedEstimatedTotal: 2000, version: 2 });
    const req = requestsFor(`/api/sales-cases/${ID}/appraisals`)[0]!;
    expect(req.method).toBe("PUT");
    expect(req.body).toEqual({ taxExcludedEstimatedTotal: 2000, version: 2 });
  });

  it("instructSalesShipping → POST /sales-cases/{id}/shipping-instruction", async () => {
    server.use(
      http.post(
        `/api/sales-cases/${ID}/shipping-instruction`,
        () => new HttpResponse(null, { status: 204 }),
      ),
    );
    await instructSalesShipping(ID, { date: "2026-05-01", version: 5 });
    const req = requestsFor(`/api/sales-cases/${ID}/shipping-instruction`)[0]!;
    expect(req.method).toBe("POST");
    expect(req.body).toEqual({ date: "2026-05-01", version: 5 });
  });

  it("completeSalesShipping → POST /sales-cases/{id}/shipping-completion", async () => {
    server.use(
      http.post(
        `/api/sales-cases/${ID}/shipping-completion`,
        () => new HttpResponse(null, { status: 204 }),
      ),
    );
    await completeSalesShipping(ID, { date: "2026-05-02", version: 6 });
    const req = requestsFor(`/api/sales-cases/${ID}/shipping-completion`)[0]!;
    expect(req.method).toBe("POST");
    expect(req.body).toEqual({ date: "2026-05-02", version: 6 });
  });

  it("deleteSalesCase → DELETE /sales-cases/{id} (body なし)", async () => {
    server.use(
      http.delete(`/api/sales-cases/${ID}`, () => new HttpResponse(null, { status: 204 })),
    );
    await deleteSalesCase(ID);
    const req = requestsFor(`/api/sales-cases/${ID}`)[0]!;
    expect(req.method).toBe("DELETE");
    expect(req.body).toBeNull();
  });

  it("409 conflict は ApiError(409) として呼び出し元へ伝播する (握り潰さない)", async () => {
    server.use(
      http.put(`/api/sales-cases/${ID}/appraisals`, () =>
        HttpResponse.json(
          { type: "optimistic-lock-conflict", title: "Conflict", status: 409, detail: "stale" },
          { status: 409 },
        ),
      ),
    );
    await expect(updateAppraisal(ID, { version: 1 })).rejects.toMatchObject({ status: 409 });
  });

  it("500 problem も ApiError として伝播する (エラーパス)", async () => {
    server.use(
      http.post(`/api/sales-cases/${ID}/shipping-instruction`, () =>
        HttpResponse.json(
          { type: "internal", title: "Internal", status: 500, detail: "boom" },
          { status: 500 },
        ),
      ),
    );
    await expect(instructSalesShipping(ID, { version: 1 })).rejects.toBeInstanceOf(ApiError);
  });
});
