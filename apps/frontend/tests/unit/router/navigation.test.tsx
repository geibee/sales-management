/**
 * Phase 6 — 本物 `routeTree.gen` を使う navigation integration (FE-NAV-*)。
 *
 * `renderWithRealRouter(initialPath)` で production のルートツリーを
 * メモリ history 起動し、以下を検査する:
 *   - FE-NAV-LOT-001: LotCreatePage 作成成功 → `/lots/{lotNumber}` の
 *     detail route が解決され、detail heading が出る
 *   - FE-NAV-SALES-001: SalesCaseCreatePage direct 作成成功 →
 *     `/sales-cases/{id}` が解決される
 *   - FE-NAV-SALES-002/003: 予約/委託 detail route が実ルートツリーで
 *     解決される。作成 form の caseType Select は jsdom で操作不能
 *     (FE-CONSTRAINT-002) のため、遷移先決定は純粋関数 `caseDetailRoute`
 *     の oracle (SalesCaseCreatePage.test.tsx) が担い、本テストは
 *     その遷移先 route が実在して描画されることを担保する
 *   - FE-NAV-AUTH-001: auth ON + role なしで protected route を直接開く
 *     → 実 route 上で Guard fallback が出る
 *
 * root route は `Shell` を描画するため、Sidebar (/lots・/sales-cases) と
 * Topbar (/health) の handler を常設する。
 */
import { useAuth } from "@/stores/auth-store";
import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it } from "vitest";
import {
  makeAvailableLot,
  makeAvailableLotsResponse,
  makeCodeMasters,
  makeConsignmentSalesCase,
  makeDirectSalesCase,
  makeLot,
  makeReservationSalesCase,
} from "../../support/fixtures";
import { renderWithRealRouter } from "../../support/render";
import { requestsFor, server } from "../../support/server";

/** Shell (Sidebar/Topbar) が常時 fetch する分の handler。 */
function mockShellChrome(): void {
  server.use(
    http.get("/api/health", () =>
      HttpResponse.json({ status: "UP", checks: { postgresql: "UP", self: "UP" } }),
    ),
    http.get("/api/lots", () => HttpResponse.json({ items: [], total: 0, limit: 1, offset: 0 })),
    http.get("/api/sales-cases", () =>
      HttpResponse.json({ items: [], total: 0, limit: 1, offset: 0 }),
    ),
  );
}

function authDisabled(): void {
  server.use(http.get("/api/auth/config", () => HttpResponse.json({ enabled: false })));
}

describe("Router integration (FE-NAV-*)", () => {
  beforeEach(() => {
    useAuth.getState().clear();
    mockShellChrome();
  });

  it("FE-NAV-LOT-001: ロット作成成功 → /lots/{lotNumber} の detail route が解決される", async () => {
    authDisabled();
    server.use(
      http.get("/api/code-masters", () => HttpResponse.json(makeCodeMasters())),
      http.post("/api/lots", () =>
        HttpResponse.json(
          { status: "manufacturing", lotNumber: "2026-A-1", version: 1 },
          { status: 201 },
        ),
      ),
      http.get("/api/lots/2026-A-1", () =>
        HttpResponse.json(makeLot({ lotNumber: "2026-A-1", status: "manufacturing" })),
      ),
    );
    renderWithRealRouter("/lots/new");
    await screen.findByLabelText("年度");
    fireEvent.click(screen.getByRole("button", { name: /作成/ }));
    // 実ルートツリー上で /lots/$id が解決され、LotDetailPage が描画される
    expect(await screen.findByRole("heading", { name: /在庫ロット 2026-A-1/ })).toBeInTheDocument();
    expect(requestsFor("/api/lots/2026-A-1")).not.toHaveLength(0);
  });

  it("FE-NAV-SALES-001: direct 案件作成成功 → /sales-cases/{id} が解決される", async () => {
    authDisabled();
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
      http.get("/api/sales-cases/2026-S-001", () =>
        HttpResponse.json(
          makeDirectSalesCase({
            salesCaseNumber: "2026-S-001",
            caseType: "direct",
            status: "before_appraisal",
          }),
        ),
      ),
    );
    renderWithRealRouter("/sales-cases/new");
    fireEvent.click(await screen.findByRole("button", { name: /ロットを選択/ }));
    fireEvent.click(await screen.findByRole("checkbox", { name: "ロット 2026-A-1 を選択" }));
    fireEvent.click(within(screen.getByRole("dialog")).getByRole("button", { name: "確定" }));
    fireEvent.change(screen.getByLabelText("販売日"), { target: { value: "2026-05-01" } });
    fireEvent.click(screen.getByRole("button", { name: /作成/ }));
    expect(await screen.findByRole("heading", { name: /販売案件 2026-S-001/ })).toBeInTheDocument();
  });

  it("FE-NAV-SALES-002: /reservation-cases/{id} が実ルートツリーで解決される", async () => {
    authDisabled();
    server.use(
      http.get("/api/sales-cases/2026-S-002", () =>
        HttpResponse.json(
          makeReservationSalesCase({
            salesCaseNumber: "2026-S-002",
            caseType: "reservation",
            status: "before_reservation",
          }),
        ),
      ),
    );
    renderWithRealRouter("/reservation-cases/2026-S-002");
    expect(
      await screen.findByRole("heading", { name: /予約販売案件 2026-S-002/ }),
    ).toBeInTheDocument();
  });

  it("FE-NAV-SALES-003: /consignment-cases/{id} が実ルートツリーで解決される", async () => {
    authDisabled();
    server.use(
      http.get("/api/sales-cases/2026-S-003", () =>
        HttpResponse.json(
          makeConsignmentSalesCase({
            salesCaseNumber: "2026-S-003",
            caseType: "consignment",
            status: "before_consignment",
          }),
        ),
      ),
    );
    renderWithRealRouter("/consignment-cases/2026-S-003");
    expect(
      await screen.findByRole("heading", { name: /委託販売案件 2026-S-003/ }),
    ).toBeInTheDocument();
  });

  it("FE-NAV-AUTH-001: auth ON + role なしで protected route → 実 route 上で Guard fallback", async () => {
    server.use(
      http.get("/api/auth/config", () =>
        HttpResponse.json({ enabled: true, authority: "https://idp", audience: "api" }),
      ),
      http.get("/api/code-masters", () => HttpResponse.json(makeCodeMasters())),
    );
    renderWithRealRouter("/lots/new");
    expect(
      await screen.findByText("作成には operator 以上のロールが必要です。"),
    ).toBeInTheDocument();
    // 作成 form は描画されない
    expect(screen.queryByLabelText("年度")).toBeNull();
    // 実 route 上に留まる (エラーページや redirect にならない)
    await waitFor(() =>
      expect(screen.getByRole("navigation", { name: "パンくず" })).toBeInTheDocument(),
    );
  });
});
