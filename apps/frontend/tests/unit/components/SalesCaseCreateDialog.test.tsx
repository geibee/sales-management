/**
 * Phase 2f — `SalesCaseCreateDialog` (FE-COMP-SALES-CREATE-DIALOG-001..005).
 *
 * The dialog is launched from LotListPage with the row-selected
 * lot numbers passed via the `lotNumbers` prop. It owns:
 *   - SWR fetch of `/code-masters` for the 事業部 dropdown
 *   - react-hook-form (caseType / divisionCode / salesDate)
 *   - POST `/sales-cases` on submit, then `useNavigate` to the case
 *     detail page
 *
 * It calls `useNavigate()` from TanStack Router, so we render the
 * dialog inside `renderWithRouter` rather than mocking the navigate
 * symbol.
 */
import { SalesCaseCreateDialog } from "@/components/sales-cases/SalesCaseCreateDialog";
import { fireEvent, screen, waitFor } from "@testing-library/react";
import { HttpResponse, http } from "msw";
import { describe, expect, it } from "vitest";
import { makeCodeMasters } from "../../support/fixtures";
import { renderWithRouter } from "../../support/render";
import { requestsFor, server } from "../../support/server";

function mockBaseHandlers(): void {
  server.use(
    http.get("/api/code-masters", () => HttpResponse.json(makeCodeMasters())),
  );
}

describe("<SalesCaseCreateDialog> (FE-COMP-SALES-CREATE-DIALOG-*)", () => {
  it("FE-COMP-SALES-CREATE-DIALOG-001: 選択された lotNumbers が dialog description に出る", async () => {
    mockBaseHandlers();
    renderWithRouter(
      <SalesCaseCreateDialog open onOpenChange={() => {}} lotNumbers={["2026-A-1", "2026-A-2"]} />,
    );
    expect(
      await screen.findByText(/選択した 2 件のロットで販売案件を起票します。/),
    ).toBeInTheDocument();
  });

  it("FE-COMP-SALES-CREATE-DIALOG-002: 事業部 dropdown に code-masters の name が並ぶ", async () => {
    mockBaseHandlers();
    renderWithRouter(
      <SalesCaseCreateDialog open onOpenChange={() => {}} lotNumbers={["2026-A-1"]} />,
    );
    // SWR が code-masters を取得するまで待つ
    await waitFor(() => expect(requestsFor("/api/code-masters")).toHaveLength(1));
    // radix-ui Select は jsdom のポインタイベントを完全には扱えないため、
    // dropdown を click で開く代わりに、空いていない状態でも DOM に挿入される
    // hidden option を取りに行く。
    const opts = await screen.findAllByRole("option", { hidden: true });
    const labels = opts.map((o) => o.textContent ?? "");
    expect(labels).toEqual(expect.arrayContaining(["営業1部", "営業2部"]));
  });

  it("FE-COMP-SALES-CREATE-DIALOG-003: submit body は lots:string[] / divisionCode:integer", async () => {
    mockBaseHandlers();
    server.use(
      http.post("/api/sales-cases", () =>
        HttpResponse.json({ salesCaseNumber: "2026-S-001", status: "before_appraisal", version: 1 }),
      ),
    );
    renderWithRouter(
      <SalesCaseCreateDialog open onOpenChange={() => {}} lotNumbers={["2026-A-1", "2026-A-2"]} />,
    );
    fireEvent.change(await screen.findByLabelText("販売日"), { target: { value: "2026-05-01" } });
    fireEvent.click(screen.getByRole("button", { name: /作成/ }));
    await waitFor(() => expect(requestsFor("/api/sales-cases")).toHaveLength(1));
    const body = requestsFor("/api/sales-cases")[0].body as {
      lots: unknown;
      divisionCode: unknown;
      salesDate: unknown;
      caseType: unknown;
    };
    expect(body.lots).toEqual(["2026-A-1", "2026-A-2"]);
    expect(typeof body.divisionCode).toBe("number");
    expect(Number.isInteger(body.divisionCode)).toBe(true);
    expect(body.salesDate).toBe("2026-05-01");
    expect(body.caseType).toBe("direct");
  });

  it("FE-COMP-SALES-CREATE-DIALOG-004: 必須空 (salesDate) → API 未呼出、field error", async () => {
    mockBaseHandlers();
    renderWithRouter(
      <SalesCaseCreateDialog open onOpenChange={() => {}} lotNumbers={["2026-A-1"]} />,
    );
    fireEvent.click(await screen.findByRole("button", { name: /作成/ }));
    // 販売日 必須 → 「販売日は yyyy-MM-dd 形式で入力してください」が出る
    expect(await screen.findByText(/販売日は yyyy-MM-dd/)).toBeInTheDocument();
    expect(requestsFor("/api/sales-cases")).toHaveLength(0);
  });

  it("FE-COMP-SALES-CREATE-DIALOG-005: API 400 後も dialog は閉じない", async () => {
    mockBaseHandlers();
    server.use(
      http.post("/api/sales-cases", () =>
        HttpResponse.json(
          { type: "validation", title: "Invalid", status: 400, detail: "lot already assigned" },
          { status: 400 },
        ),
      ),
    );
    renderWithRouter(
      <SalesCaseCreateDialog open onOpenChange={() => {}} lotNumbers={["2026-A-1"]} />,
    );
    fireEvent.change(await screen.findByLabelText("販売日"), { target: { value: "2026-05-01" } });
    fireEvent.click(screen.getByRole("button", { name: /作成/ }));
    await waitFor(() => expect(requestsFor("/api/sales-cases")).toHaveLength(1));
    // dialog はまだ open
    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });
});
