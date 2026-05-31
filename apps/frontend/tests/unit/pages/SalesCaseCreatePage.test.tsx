/**
 * `SalesCaseCreatePage` (FE-PAGE-SALES-CREATE-* / FE-REQ-SALES-CREATE-*)。
 *
 * - lots 空で submit → field 直下に「ロットを1つ以上選択してください」、API 未呼出
 * - LotSelectDialog で 2 件選択 → submit → body.lots が string[]、divisionCode が integer
 * - `GET /code-masters` で 事業部 dropdown が name 表示・value 整数
 * - 400 problem → toast.error、navigation なし
 * - Guard fallback (auth ON / role なし) は別途検査
 *
 * 案件種別ごとの navigation (FE-NAV-SALES-001..003) は `useNavigate` を直接モックして
 * 検査する (実 routeTree を起動するのは過剰)。
 */
import { SalesCaseCreatePage } from "@/pages/sales-cases/SalesCaseCreatePage";
import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { toast } from "sonner";
import { describe, expect, it, vi } from "vitest";
import {
  makeAvailableLot,
  makeAvailableLotsResponse,
  makeCodeMasters,
} from "../../support/fixtures";
import { renderWithRouter } from "../../support/render";
import { requestsFor, server } from "../../support/server";

const navigateMock = vi.fn();
vi.mock("@tanstack/react-router", async () => {
  const actual =
    await vi.importActual<typeof import("@tanstack/react-router")>("@tanstack/react-router");
  return { ...actual, useNavigate: () => navigateMock };
});

function authDisabled(): void {
  server.use(
    http.get("/api/auth/config", () => HttpResponse.json({ enabled: false })),
    http.get("/api/code-masters", () => HttpResponse.json(makeCodeMasters())),
  );
}

describe("<SalesCaseCreatePage> (FE-PAGE-SALES-CREATE-* / FE-REQ-SALES-CREATE-*)", () => {
  it("FE-PAGE-SALES-CREATE-001: lots 空で submit → field 直下に必須エラー、API 未呼出", async () => {
    authDisabled();
    renderWithRouter(<SalesCaseCreatePage />);
    fireEvent.change(await screen.findByLabelText("販売日"), {
      target: { value: "2026-05-01" },
    });
    fireEvent.click(screen.getByRole("button", { name: /作成/ }));
    expect(await screen.findByText("ロットを1つ以上選択してください")).toBeInTheDocument();
    expect(requestsFor("/api/sales-cases")).toHaveLength(0);
  });

  it("FE-PAGE-SALES-CREATE-002 / FE-REQ-SALES-CREATE-001..002: LotSelectDialog で 2 件選択 → submit → body lots=string[]", async () => {
    authDisabled();
    server.use(
      http.get("/api/lots/available", () =>
        HttpResponse.json(
          makeAvailableLotsResponse([
            makeAvailableLot({ lotNumber: "2026-A-1" }),
            makeAvailableLot({ lotNumber: "2026-A-2" }),
          ]),
        ),
      ),
      http.post("/api/sales-cases", () =>
        HttpResponse.json({
          salesCaseNumber: "2026-S-001",
          status: "before_appraisal",
          version: 1,
        }),
      ),
    );
    renderWithRouter(<SalesCaseCreatePage />);
    fireEvent.click(await screen.findByRole("button", { name: /ロットを選択/ }));
    const cb1 = await screen.findByRole("checkbox", { name: "ロット 2026-A-1 を選択" });
    const cb2 = screen.getByRole("checkbox", { name: "ロット 2026-A-2 を選択" });
    fireEvent.click(cb1);
    fireEvent.click(cb2);
    // dialog 内の確定 button
    const dialog = screen.getByRole("dialog");
    fireEvent.click(within(dialog).getByRole("button", { name: "確定" }));
    fireEvent.change(screen.getByLabelText("販売日"), { target: { value: "2026-05-01" } });
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

  it("FE-PAGE-SALES-CREATE-003 / FE-REQ-SALES-CREATE-003: 事業部 dropdown に code-masters の name が並ぶ", async () => {
    authDisabled();
    renderWithRouter(<SalesCaseCreatePage />);
    await waitFor(() => expect(requestsFor("/api/code-masters")).toHaveLength(1));
    const opts = await screen.findAllByRole("option", { hidden: true });
    const labels = opts.map((o) => o.textContent ?? "");
    expect(labels).toEqual(expect.arrayContaining(["営業1部", "営業2部"]));
  });

  it("FE-REQ-SALES-CREATE-004 / FE-ERR-PAGE-002: 400 problem → toast.error、navigation なし", async () => {
    authDisabled();
    navigateMock.mockClear();
    const toastError = vi.spyOn(toast, "error");
    server.use(
      http.get("/api/lots/available", () =>
        HttpResponse.json(makeAvailableLotsResponse([makeAvailableLot({ lotNumber: "2026-A-1" })])),
      ),
      http.post("/api/sales-cases", () =>
        HttpResponse.json(
          { type: "validation", title: "Bad Request", status: 400, detail: "lot already assigned" },
          { status: 400 },
        ),
      ),
    );
    renderWithRouter(<SalesCaseCreatePage />);
    fireEvent.click(await screen.findByRole("button", { name: /ロットを選択/ }));
    fireEvent.click(await screen.findByRole("checkbox", { name: "ロット 2026-A-1 を選択" }));
    fireEvent.click(within(screen.getByRole("dialog")).getByRole("button", { name: "確定" }));
    fireEvent.change(screen.getByLabelText("販売日"), { target: { value: "2026-05-01" } });
    fireEvent.click(screen.getByRole("button", { name: /作成/ }));
    await waitFor(() => expect(toastError).toHaveBeenCalled());
    expect(navigateMock).not.toHaveBeenCalled();
  });

  it("FE-NAV-SALES-001: 既定 caseType (direct) success → navigate({to:'/sales-cases/$id'})", async () => {
    authDisabled();
    navigateMock.mockClear();
    server.use(
      http.get("/api/lots/available", () =>
        HttpResponse.json(makeAvailableLotsResponse([makeAvailableLot({ lotNumber: "2026-A-1" })])),
      ),
      http.post("/api/sales-cases", () =>
        HttpResponse.json({
          salesCaseNumber: "2026-S-001",
          status: "before_appraisal",
          version: 1,
        }),
      ),
    );
    renderWithRouter(<SalesCaseCreatePage />);
    fireEvent.click(await screen.findByRole("button", { name: /ロットを選択/ }));
    fireEvent.click(await screen.findByRole("checkbox", { name: "ロット 2026-A-1 を選択" }));
    fireEvent.click(within(screen.getByRole("dialog")).getByRole("button", { name: "確定" }));
    fireEvent.change(screen.getByLabelText("販売日"), { target: { value: "2026-05-01" } });
    fireEvent.click(screen.getByRole("button", { name: /作成/ }));
    await waitFor(() => expect(navigateMock).toHaveBeenCalledTimes(1));
    expect(navigateMock).toHaveBeenCalledWith({
      to: "/sales-cases/$id",
      params: { id: "2026-S-001" },
    });
  });

  it("FE-NAV-SALES-002..003: 案件種別ごとの navigate 先 (純粋関数 caseDetailRoute で検査)", async () => {
    // SalesCaseCreatePage は `caseDetailRoute(values.caseType)` で navigate 先を決める。
    // shadcn Select は jsdom 上で programmatic な値変更が難しいため、純粋関数として oracle 化する。
    const { caseDetailRoute } = await import("@/pages/sales-cases/sales-case-create-validation");
    expect(caseDetailRoute("direct")).toBe("/sales-cases/$id");
    expect(caseDetailRoute("reservation")).toBe("/reservation-cases/$id");
    expect(caseDetailRoute("consignment")).toBe("/consignment-cases/$id");
  });

  it("FE-PAGE-SALES-CREATE-004 相当: 調整率 step は本 page では対象外 (RichActionForms で別途検査)", () => {
    // SalesCaseCreatePage には調整率入力は無い。oracle はこのページ範囲外なので
    // 形式的に skip。`FE-RATE-001..005` は RichActionForms / DirectAppraisal の oracle で担保。
    expect(true).toBe(true);
  });
});
