/**
 * Phase 2e — `LotSelectDialog` (FE-COMP-LOT-SELECT-001..006).
 *
 * The dialog is the shared lot picker used by SalesCaseCreatePage /
 * SalesCaseCreateDialog / 「ロットを修正」. It owns:
 *   - SWR fetch of `/lots/available` (or `?excludeCase=...`) gated on
 *     `open`
 *   - a multi-row table with one checkbox per item (aria-label =
 *     `ロット {lotNumber} を選択`)
 *   - a confirm button disabled while no row is checked
 *
 * MSW request capture lets us assert URL and query shape without
 * mocking `useAvailableLots`.
 */
import { LotSelectDialog } from "@/components/lots/LotSelectDialog";
import { fireEvent, screen, waitFor } from "@testing-library/react";
import { HttpResponse, http } from "msw";
import { describe, expect, it, vi } from "vitest";
import { deferred } from "../../support/deferred";
import { makeAvailableLot, makeAvailableLotsResponse } from "../../support/fixtures";
import { renderWithApp } from "../../support/render";
import { requestsFor, server } from "../../support/server";

function renderDialog(props: Partial<Parameters<typeof LotSelectDialog>[0]> = {}) {
  const onOpenChange = vi.fn();
  const onConfirm = vi.fn();
  renderWithApp(
    <LotSelectDialog
      open
      onOpenChange={onOpenChange}
      value={[]}
      onConfirm={onConfirm}
      {...props}
    />,
  );
  return { onOpenChange, onConfirm };
}

describe("<LotSelectDialog> (FE-COMP-LOT-SELECT-*)", () => {
  it("FE-COMP-LOT-SELECT-001: open 時に GET /lots/available を呼び、行 + checkbox が出る", async () => {
    server.use(
      http.get("/api/lots/available", () =>
        HttpResponse.json(
          makeAvailableLotsResponse([
            makeAvailableLot({ lotNumber: "2026-A-1" }),
            makeAvailableLot({ lotNumber: "2026-A-2" }),
          ]),
        ),
      ),
    );
    renderDialog();
    expect(await screen.findByRole("checkbox", { name: "ロット 2026-A-1 を選択" })).toBeInTheDocument();
    expect(screen.getByRole("checkbox", { name: "ロット 2026-A-2 を選択" })).toBeInTheDocument();
    // 初期状態は全 unchecked
    for (const cb of screen.getAllByRole("checkbox")) {
      expect(cb).not.toBeChecked();
    }
    await waitFor(() => expect(requestsFor("/api/lots/available")).toHaveLength(1));
  });

  it("FE-COMP-LOT-SELECT-002: excludeCase 付き open → query に excludeCase が入る", async () => {
    server.use(
      http.get("/api/lots/available", () => HttpResponse.json(makeAvailableLotsResponse([]))),
    );
    renderDialog({ excludeCase: "2026-S-001" });
    await waitFor(() => expect(requestsFor("/api/lots/available").length).toBeGreaterThanOrEqual(1));
    const call = requestsFor("/api/lots/available")[0];
    expect(call.search).toContain("excludeCase=2026-S-001");
  });

  it("FE-COMP-LOT-SELECT-003: 選択 → 確定 → onConfirm(selectedIds)", async () => {
    server.use(
      http.get("/api/lots/available", () =>
        HttpResponse.json(
          makeAvailableLotsResponse([
            makeAvailableLot({ lotNumber: "2026-A-1" }),
            makeAvailableLot({ lotNumber: "2026-A-2" }),
          ]),
        ),
      ),
    );
    const { onConfirm } = renderDialog();
    const cb1 = await screen.findByRole("checkbox", { name: "ロット 2026-A-1 を選択" });
    const cb2 = screen.getByRole("checkbox", { name: "ロット 2026-A-2 を選択" });
    fireEvent.click(cb1);
    fireEvent.click(cb2);
    fireEvent.click(screen.getByRole("button", { name: "確定" }));
    expect(onConfirm).toHaveBeenCalledTimes(1);
    expect(onConfirm).toHaveBeenCalledWith(["2026-A-1", "2026-A-2"]);
  });

  it("FE-COMP-LOT-SELECT-004: 選択 0 件で 確定 button disabled", async () => {
    server.use(
      http.get("/api/lots/available", () =>
        HttpResponse.json(makeAvailableLotsResponse([makeAvailableLot()])),
      ),
    );
    renderDialog();
    expect(await screen.findByRole("button", { name: "確定" })).toBeDisabled();
  });

  it("FE-COMP-LOT-SELECT-005: API 500 → エラー表示", async () => {
    server.use(
      http.get("/api/lots/available", () =>
        HttpResponse.json(
          { type: "internal-error", title: "Internal", status: 500, detail: "boom" },
          { status: 500 },
        ),
      ),
    );
    renderDialog();
    expect(await screen.findByText(/エラー:/)).toBeInTheDocument();
  });

  it("FE-COMP-LOT-SELECT-006: pending 中は `読み込み中…` 表示", async () => {
    const d = deferred<Response>();
    server.use(http.get("/api/lots/available", () => d.promise));
    renderDialog();
    expect(await screen.findByText("読み込み中…")).toBeInTheDocument();
    d.resolve(HttpResponse.json(makeAvailableLotsResponse([])) as unknown as Response);
  });
});
