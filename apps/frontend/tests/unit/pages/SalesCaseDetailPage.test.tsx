/**
 * `SalesCaseDetailPage` (FE-PAGE-SALES-DETAIL-* / FE-REQ-SALES-ACTION-* /
 *  FE-REQ-SALES-LOTS-* / FE-VERSION-SALES-*)。
 *
 * 検査するのは以下:
 *   - loading / success / error 表示
 *   - 「ロットを修正」: before_appraisal のみ表示、価格登録後は非表示
 *   - LotSelectDialog 経由で PUT /sales-cases/{id}/lots を呼ぶ
 *     (body: lots:string[], version)
 *   - LotSelectDialog open 時 GET /lots/available?excludeCase={id}
 *   - DELETE /sales-cases/{id}/appraisals は body に version を含む
 *   - DELETE 系 action は confirm を介して呼ばれる
 *   - 409 は toast.error、navigation なし
 */
import { schemas } from "@/contracts";
import { SalesCaseDetailPage } from "@/pages/sales-cases/SalesCaseDetailPage";
import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { toast } from "sonner";
import { describe, expect, it, vi } from "vitest";
import { deferred } from "../../support/deferred";
import {
  makeAvailableLot,
  makeAvailableLotsResponse,
  makeDirectSalesCase,
} from "../../support/fixtures";
import { renderWithRouter } from "../../support/render";
import { requestsFor, server } from "../../support/server";

const ID = "2026-S-001";

function authDisabled(): void {
  server.use(http.get("/api/auth/config", () => HttpResponse.json({ enabled: false })));
}

describe("<SalesCaseDetailPage> (FE-PAGE-SALES-DETAIL-* / FE-REQ-SALES-*)", () => {
  it("FE-PAGE-SALES-DETAIL-001: GET pending → `読み込み中…`", async () => {
    authDisabled();
    const d = deferred<Response>();
    server.use(http.get(`/api/sales-cases/${ID}`, () => d.promise));
    renderWithRouter(<SalesCaseDetailPage id={ID} />);
    expect(await screen.findByText("読み込み中…")).toBeInTheDocument();
    d.resolve(
      HttpResponse.json(makeDirectSalesCase({ salesCaseNumber: ID })) as unknown as Response,
    );
  });

  it("FE-PAGE-SALES-DETAIL-002: success → heading / badge / 状態フロー", async () => {
    authDisabled();
    server.use(
      http.get(`/api/sales-cases/${ID}`, () =>
        HttpResponse.json(makeDirectSalesCase({ salesCaseNumber: ID, status: "before_appraisal" })),
      ),
    );
    renderWithRouter(<SalesCaseDetailPage id={ID} />);
    expect(
      await screen.findByRole("heading", { name: new RegExp(`販売案件 ${ID}`) }),
    ).toBeInTheDocument();
    expect(screen.getAllByText("査定前").length).toBeGreaterThan(0);
  });

  it("FE-PAGE-SALES-DETAIL-003: GET 500 → エラー文言", async () => {
    authDisabled();
    server.use(
      http.get(`/api/sales-cases/${ID}`, () =>
        HttpResponse.json(
          { type: "internal", title: "Internal", status: 500, detail: "boom" },
          { status: 500 },
        ),
      ),
    );
    renderWithRouter(<SalesCaseDetailPage id={ID} />);
    expect(await screen.findByText(/エラー:/)).toBeInTheDocument();
  });

  it("FE-REQ-SALES-LOTS-001 / FE-REQ-SALES-LOTS-003: ロット修正 → PUT body と GET ?excludeCase", async () => {
    authDisabled();
    server.use(
      http.get(`/api/sales-cases/${ID}`, () =>
        HttpResponse.json(
          makeDirectSalesCase({
            salesCaseNumber: ID,
            status: "before_appraisal",
            caseType: "direct",
            lots: ["2026-A-1"],
            version: 5,
          }),
        ),
      ),
      http.get("/api/lots/available", () =>
        HttpResponse.json(
          makeAvailableLotsResponse([
            makeAvailableLot({ lotNumber: "2026-A-1" }),
            makeAvailableLot({ lotNumber: "2026-A-2" }),
          ]),
        ),
      ),
      http.put(`/api/sales-cases/${ID}/lots`, () => new HttpResponse(null, { status: 204 })),
    );
    renderWithRouter(<SalesCaseDetailPage id={ID} />);
    fireEvent.click(await screen.findByRole("button", { name: /ロットを修正/ }));
    // GET /lots/available?excludeCase=ID
    await waitFor(() =>
      expect(requestsFor("/api/lots/available").length).toBeGreaterThanOrEqual(1),
    );
    expect(requestsFor("/api/lots/available")[0]!.search).toContain(`excludeCase=${ID}`);
    // 新規ロットを 1 件追加して確定
    const dialog = screen.getByRole("dialog");
    fireEvent.click(
      await within(dialog).findByRole("checkbox", { name: "ロット 2026-A-2 を選択" }),
    );
    fireEvent.click(within(dialog).getByRole("button", { name: "更新" }));
    await waitFor(() => expect(requestsFor(`/api/sales-cases/${ID}/lots`)).toHaveLength(1));
    const body = requestsFor(`/api/sales-cases/${ID}/lots`)[0]!.body as {
      lots: unknown;
      version: unknown;
    };
    expect(Array.isArray(body.lots)).toBe(true);
    expect((body.lots as unknown[]).every((l) => typeof l === "string")).toBe(true);
    expect(body.version).toBe(5);
  });

  it("FE-REQ-SALES-LOTS-004: PUT /sales-cases/{id}/lots が 409 → toast.error (refetch は FE-CONSTRAINT-001)", async () => {
    authDisabled();
    const toastError = vi.spyOn(toast, "error");
    server.use(
      http.get(`/api/sales-cases/${ID}`, () =>
        HttpResponse.json(
          makeDirectSalesCase({
            salesCaseNumber: ID,
            status: "before_appraisal",
            caseType: "direct",
            lots: ["2026-A-1"],
            version: 5,
          }),
        ),
      ),
      http.get("/api/lots/available", () =>
        HttpResponse.json(
          makeAvailableLotsResponse([
            makeAvailableLot({ lotNumber: "2026-A-1" }),
            makeAvailableLot({ lotNumber: "2026-A-2" }),
          ]),
        ),
      ),
      http.put(`/api/sales-cases/${ID}/lots`, () =>
        HttpResponse.json(
          { type: "conflict", title: "Conflict", status: 409, detail: "version mismatch" },
          { status: 409 },
        ),
      ),
    );
    renderWithRouter(<SalesCaseDetailPage id={ID} />);
    fireEvent.click(await screen.findByRole("button", { name: /ロットを修正/ }));
    const dialog = await screen.findByRole("dialog");
    fireEvent.click(
      await within(dialog).findByRole("checkbox", { name: "ロット 2026-A-2 を選択" }),
    );
    fireEvent.click(within(dialog).getByRole("button", { name: "更新" }));
    await waitFor(() => expect(requestsFor(`/api/sales-cases/${ID}/lots`)).toHaveLength(1));
    await waitFor(() => expect(toastError).toHaveBeenCalled());
    // page は detail 表示のまま残る
    expect(screen.getByRole("heading", { name: new RegExp(ID) })).toBeInTheDocument();
  });

  it("FE-REQ-SALES-LOTS-002: 価格登録後 (status=appraised) は「ロットを修正」が出ない", async () => {
    authDisabled();
    server.use(
      http.get(`/api/sales-cases/${ID}`, () =>
        HttpResponse.json(
          makeDirectSalesCase({
            salesCaseNumber: ID,
            status: "appraised",
            caseType: "direct",
            lots: ["2026-A-1"],
          }),
        ),
      ),
    );
    renderWithRouter(<SalesCaseDetailPage id={ID} />);
    await screen.findByRole("heading", { name: new RegExp(ID) });
    expect(screen.queryByRole("button", { name: /ロットを修正/ })).toBeNull();
  });

  it("FE-REQ-SALES-ACTION-002 / FE-VERSION-SALES-001: 価格査定 削除 → DELETE body に version", async () => {
    authDisabled();
    vi.spyOn(window, "confirm").mockReturnValue(true);
    server.use(
      http.get(`/api/sales-cases/${ID}`, () =>
        HttpResponse.json(
          makeDirectSalesCase({
            salesCaseNumber: ID,
            status: "appraised",
            caseType: "direct",
            version: 9,
          }),
        ),
      ),
      http.delete(
        `/api/sales-cases/${ID}/appraisals`,
        () => new HttpResponse(null, { status: 204 }),
      ),
    );
    renderWithRouter(<SalesCaseDetailPage id={ID} />);
    // 「価格査定 削除」 SimpleAction の「実行」 button
    const card = (await screen.findByText("価格査定 削除")).closest('[data-slot="card"]')!;
    fireEvent.click(within(card as HTMLElement).getByRole("button", { name: /実行/ }));
    await waitFor(() => expect(requestsFor(`/api/sales-cases/${ID}/appraisals`)).toHaveLength(1));
    expect(requestsFor(`/api/sales-cases/${ID}/appraisals`)[0]!.method).toBe("DELETE");
    expect(requestsFor(`/api/sales-cases/${ID}/appraisals`)[0]!.body).toEqual({ version: 9 });
  });

  it("FE-REQ-SALES-ACTION-001: 価格査定 登録 → POST body の各 rate は ÷100 (0.9〜1.1) で契約適合", async () => {
    authDisabled();
    server.use(
      http.get(`/api/sales-cases/${ID}`, () =>
        HttpResponse.json(
          makeDirectSalesCase({
            salesCaseNumber: ID,
            status: "before_appraisal",
            caseType: "direct",
            lots: ["2026-A-1"],
            version: 4,
          }),
        ),
      ),
      http.post(`/api/sales-cases/${ID}/appraisals`, () =>
        HttpResponse.json({ salesCaseNumber: ID, status: "appraised", version: 5 }),
      ),
    );
    renderWithRouter(<SalesCaseDetailPage id={ID} />);
    const card = (await screen.findByText("価格査定 登録")).closest(
      '[data-slot="card"]',
    )! as HTMLElement;
    fireEvent.change(within(card).getByLabelText("期中調整率(%)"), { target: { value: "95" } });
    fireEvent.change(within(card).getByLabelText("取引先調整率(%)"), {
      target: { value: "105" },
    });
    fireEvent.click(within(card).getByRole("button", { name: "登録" }));
    await waitFor(() => expect(requestsFor(`/api/sales-cases/${ID}/appraisals`)).toHaveLength(1));
    // 契約 request schema でそのまま parse できる = rate が 0.9〜1.1 に収まっている
    const body = schemas.createSalesAppraisal_Body.parse(
      requestsFor(`/api/sales-cases/${ID}/appraisals`)[0]!.body,
    );
    expect(body.lotAppraisals[0]!.detailAppraisals[0]!.periodAdjustmentRate).toBeCloseTo(0.95, 5);
    expect(body.lotAppraisals[0]!.detailAppraisals[0]!.counterpartyAdjustmentRate).toBeCloseTo(
      1.05,
      5,
    );
    expect(body.version).toBe(4);
  });

  it("FE-REQ-SALES-ACTION-003: 売買契約 登録 → POST body の契約調整率は ÷100 で契約適合", async () => {
    authDisabled();
    server.use(
      http.get(`/api/sales-cases/${ID}`, () =>
        HttpResponse.json(
          makeDirectSalesCase({
            salesCaseNumber: ID,
            status: "appraised",
            caseType: "direct",
            version: 6,
          }),
        ),
      ),
      http.post(`/api/sales-cases/${ID}/contracts`, () =>
        HttpResponse.json({ salesCaseNumber: ID, status: "contracted", version: 7 }),
      ),
    );
    renderWithRouter(<SalesCaseDetailPage id={ID} />);
    const card = (await screen.findByText("売買契約 登録")).closest(
      '[data-slot="card"]',
    )! as HTMLElement;
    fireEvent.change(within(card).getByLabelText("契約調整率(%)"), { target: { value: "105" } });
    fireEvent.click(within(card).getByRole("button", { name: "登録" }));
    await waitFor(() => expect(requestsFor(`/api/sales-cases/${ID}/contracts`)).toHaveLength(1));
    const raw = requestsFor(`/api/sales-cases/${ID}/contracts`)[0]!.body as {
      contractAdjustmentRate: number;
    };
    // 契約 request schema (passthrough) に適合しつつ、調整率は ÷100 換算で送る
    const body = schemas.createSalesContract_Body.parse(raw);
    expect(raw.contractAdjustmentRate).toBeCloseTo(1.05, 5);
    expect(body.version).toBe(6);
  });

  it("FE-PAGE-SALES-DETAIL-APPRAISAL-001: 査定合計が page 上で wire-up され、単価変更で即再計算される", async () => {
    authDisabled();
    server.use(
      http.get(`/api/sales-cases/${ID}`, () =>
        HttpResponse.json(
          makeDirectSalesCase({
            salesCaseNumber: ID,
            status: "before_appraisal",
            caseType: "direct",
            lots: ["2026-A-1", "2026-A-2"],
            version: 4,
          }),
        ),
      ),
    );
    renderWithRouter(<SalesCaseDetailPage id={ID} />);
    const card = (await screen.findByText("価格査定 登録")).closest(
      '[data-slot="card"]',
    )! as HTMLElement;
    // 初期値: 単価1000 × rate 1.0 × 1.0 × 2 ロット = 2,000
    expect(within(card).getByText(/2,000/)).toBeInTheDocument();
    // 1 ロット目の単価を 3000 に → 3000 + 1000 = 4,000 (BR-APPRAISAL-TOTAL-FORMULA)
    fireEvent.change(within(card).getAllByLabelText("基準単価")[0]!, {
      target: { value: "3000" },
    });
    await waitFor(() => expect(within(card).getByText(/4,000/)).toBeInTheDocument());
  });

  it("FE-VERSION-SALES-002: 売買契約 削除 → DELETE body に version", async () => {
    authDisabled();
    vi.spyOn(window, "confirm").mockReturnValue(true);
    server.use(
      http.get(`/api/sales-cases/${ID}`, () =>
        HttpResponse.json(
          makeDirectSalesCase({
            salesCaseNumber: ID,
            status: "contracted",
            caseType: "direct",
            version: 8,
          }),
        ),
      ),
      http.delete(
        `/api/sales-cases/${ID}/contracts`,
        () => new HttpResponse(null, { status: 204 }),
      ),
    );
    renderWithRouter(<SalesCaseDetailPage id={ID} />);
    const card = (await screen.findByText("売買契約 削除")).closest(
      '[data-slot="card"]',
    )! as HTMLElement;
    fireEvent.click(within(card).getByRole("button", { name: /実行/ }));
    await waitFor(() => expect(requestsFor(`/api/sales-cases/${ID}/contracts`)).toHaveLength(1));
    expect(requestsFor(`/api/sales-cases/${ID}/contracts`)[0]!.method).toBe("DELETE");
    expect(requestsFor(`/api/sales-cases/${ID}/contracts`)[0]!.body).toEqual({ version: 8 });
  });

  it("FE-VERSION-SALES-003: 出荷指示 解除 → DELETE body に version", async () => {
    authDisabled();
    vi.spyOn(window, "confirm").mockReturnValue(true);
    server.use(
      http.get(`/api/sales-cases/${ID}`, () =>
        HttpResponse.json(
          makeDirectSalesCase({
            salesCaseNumber: ID,
            status: "shipping_instructed",
            caseType: "direct",
            version: 11,
          }),
        ),
      ),
      http.delete(
        `/api/sales-cases/${ID}/shipping-instruction`,
        () => new HttpResponse(null, { status: 204 }),
      ),
    );
    renderWithRouter(<SalesCaseDetailPage id={ID} />);
    const card = (await screen.findByText("出荷指示 解除")).closest(
      '[data-slot="card"]',
    )! as HTMLElement;
    fireEvent.click(within(card).getByRole("button", { name: /実行/ }));
    await waitFor(() =>
      expect(requestsFor(`/api/sales-cases/${ID}/shipping-instruction`)).toHaveLength(1),
    );
    expect(requestsFor(`/api/sales-cases/${ID}/shipping-instruction`)[0]!.method).toBe("DELETE");
    expect(requestsFor(`/api/sales-cases/${ID}/shipping-instruction`)[0]!.body).toEqual({
      version: 11,
    });
  });

  it("FE-ERR-PAGE-001: 価格査定 削除 で 409 → toast.error、page は残る", async () => {
    authDisabled();
    vi.spyOn(window, "confirm").mockReturnValue(true);
    const toastError = vi.spyOn(toast, "error");
    server.use(
      http.get(`/api/sales-cases/${ID}`, () =>
        HttpResponse.json(
          makeDirectSalesCase({
            salesCaseNumber: ID,
            status: "appraised",
            caseType: "direct",
            version: 9,
          }),
        ),
      ),
      http.delete(`/api/sales-cases/${ID}/appraisals`, () =>
        HttpResponse.json(
          { type: "optimistic-lock-conflict", title: "Conflict", status: 409, detail: "stale" },
          { status: 409 },
        ),
      ),
    );
    renderWithRouter(<SalesCaseDetailPage id={ID} />);
    const card = (await screen.findByText("価格査定 削除")).closest('[data-slot="card"]')!;
    fireEvent.click(within(card as HTMLElement).getByRole("button", { name: /実行/ }));
    await waitFor(() => expect(toastError).toHaveBeenCalled());
    expect(screen.getByRole("heading", { name: new RegExp(ID) })).toBeInTheDocument();
  });
});
