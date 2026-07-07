/**
 * `ConsignmentCaseDetailPage` (FE-PAGE-CONSIGNMENT-001 /
 *  FE-REQ-CONSIGNMENT-* / FE-REQ-CONSIGNMENT-LOTS-* / FE-VERSION-CON-001)。
 *
 * - 200 success → status badge と JSON pre
 * - DELETE /sales-cases/{id}/consignment/designation で body に version
 * - before_consignment では「ロットを修正」が出る → PUT /lots
 * - 委託指定済後は「ロットを修正」が消える
 */
import { schemas } from "@/contracts";
import { ConsignmentCaseDetailPage } from "@/pages/consignment-cases/ConsignmentCaseDetailPage";
import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import {
  makeAvailableLot,
  makeAvailableLotsResponse,
  makeConsignmentSalesCase,
} from "../../support/fixtures";
import { renderWithRouter } from "../../support/render";
import { requestsFor, server } from "../../support/server";

const ID = "2026-S-003";

function authDisabled(): void {
  server.use(http.get("/api/auth/config", () => HttpResponse.json({ enabled: false })));
}

describe("<ConsignmentCaseDetailPage> (FE-PAGE-CONSIGNMENT-* / FE-REQ-CONSIGNMENT-*)", () => {
  it("FE-PAGE-CONSIGNMENT-001: 200 → status label", async () => {
    authDisabled();
    server.use(
      http.get(`/api/sales-cases/${ID}`, () =>
        HttpResponse.json(
          makeConsignmentSalesCase({
            salesCaseNumber: ID,
            caseType: "consignment",
            status: "consignment_designated",
          }),
        ),
      ),
    );
    renderWithRouter(<ConsignmentCaseDetailPage id={ID} />);
    expect(
      await screen.findByRole("heading", { name: new RegExp(`委託販売案件 ${ID}`) }),
    ).toBeInTheDocument();
    expect(screen.getByText("委託指定済")).toBeInTheDocument();
  });

  it("FE-REQ-CONSIGNMENT-002 / FE-VERSION-CON-001: 委託指定解除 → DELETE body に version", async () => {
    authDisabled();
    vi.spyOn(window, "confirm").mockReturnValue(true);
    server.use(
      http.get(`/api/sales-cases/${ID}`, () =>
        HttpResponse.json(
          makeConsignmentSalesCase({
            salesCaseNumber: ID,
            caseType: "consignment",
            status: "consignment_designated",
            version: 11,
          }),
        ),
      ),
      http.delete(
        `/api/sales-cases/${ID}/consignment/designation`,
        () => new HttpResponse(null, { status: 204 }),
      ),
    );
    renderWithRouter(<ConsignmentCaseDetailPage id={ID} />);
    fireEvent.click(await screen.findByRole("button", { name: /解除/ }));
    await waitFor(() =>
      expect(requestsFor(`/api/sales-cases/${ID}/consignment/designation`)).toHaveLength(1),
    );
    expect(requestsFor(`/api/sales-cases/${ID}/consignment/designation`)[0]!.body).toEqual({
      version: 11,
    });
  });

  it("FE-REQ-CONSIGNMENT-001: 委託指定 → POST body が契約 request schema に適合 (rate フィールドは契約に無いため対象外)", async () => {
    authDisabled();
    server.use(
      http.get(`/api/sales-cases/${ID}`, () =>
        HttpResponse.json(
          makeConsignmentSalesCase({
            salesCaseNumber: ID,
            caseType: "consignment",
            status: "before_consignment",
            lots: ["2026-A-1"],
            version: 2,
          }),
        ),
      ),
      http.post(`/api/sales-cases/${ID}/consignment/designate`, () =>
        HttpResponse.json({ status: "consignment_designated", version: 3 }),
      ),
    );
    renderWithRouter(<ConsignmentCaseDetailPage id={ID} />);
    // 「委託指定」text は StatusFlow の step label と衝突するため、
    // form 固有の「委託先名」input から card を特定する
    const nameInput = await screen.findByLabelText("委託先名");
    const card = nameInput.closest('[data-slot="card"]')! as HTMLElement;
    fireEvent.change(nameInput, { target: { value: "委託先B" } });
    fireEvent.click(within(card).getByRole("button", { name: "登録" }));
    await waitFor(() =>
      expect(requestsFor(`/api/sales-cases/${ID}/consignment/designate`)).toHaveLength(1),
    );
    const body = schemas.designateConsignment_Body.parse(
      requestsFor(`/api/sales-cases/${ID}/consignment/designate`)[0]!.body,
    );
    expect(body.consignorName).toBe("委託先B");
    expect(body.version).toBe(2);
  });

  it("FE-REQ-CONSIGNMENT-LOTS-001: before_consignment → 「ロットを修正」→ PUT body lots+version", async () => {
    authDisabled();
    server.use(
      http.get(`/api/sales-cases/${ID}`, () =>
        HttpResponse.json(
          makeConsignmentSalesCase({
            salesCaseNumber: ID,
            caseType: "consignment",
            status: "before_consignment",
            lots: ["2026-A-1"],
            version: 4,
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
    renderWithRouter(<ConsignmentCaseDetailPage id={ID} />);
    fireEvent.click(await screen.findByRole("button", { name: /ロットを修正/ }));
    const dialog = await screen.findByRole("dialog");
    expect(requestsFor("/api/lots/available")[0]!.search).toContain(`excludeCase=${ID}`);
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
    expect(body.version).toBe(4);
  });

  it("FE-REQ-CONSIGNMENT-LOTS-002: 委託指定後 → 「ロットを修正」非表示", async () => {
    authDisabled();
    server.use(
      http.get(`/api/sales-cases/${ID}`, () =>
        HttpResponse.json(
          makeConsignmentSalesCase({
            salesCaseNumber: ID,
            caseType: "consignment",
            status: "consignment_designated",
            lots: ["2026-A-1"],
          }),
        ),
      ),
    );
    renderWithRouter(<ConsignmentCaseDetailPage id={ID} />);
    await screen.findByRole("heading", { name: new RegExp(ID) });
    expect(screen.queryByRole("button", { name: /ロットを修正/ })).toBeNull();
  });
});
