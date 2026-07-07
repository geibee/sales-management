/**
 * `/` (routes/index.tsx ダッシュボード) — issue #9 §7 の未テスト部。
 * route component のため `renderWithRealRouter("/")` で実ルートごと描画する。
 *
 * - KPI (ロット総数 / 製造完了 / 査定待ち / 案件総数) が list API から集計される
 * - 直近ロット feed が lotNumber リンクを描画する
 * - items 空のとき EmptyState を出す
 */
import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it } from "vitest";
import { renderWithRealRouter } from "../../support/render";
import { server } from "../../support/server";

function mockChrome(): void {
  server.use(
    http.get("/api/health", () =>
      HttpResponse.json({ status: "UP", checks: { postgresql: "UP", self: "UP" } }),
    ),
    http.get("/api/auth/config", () => HttpResponse.json({ enabled: false })),
  );
}

describe("<HomePage> (routes/index)", () => {
  beforeEach(mockChrome);

  it("list API の集計から KPI と直近ロット feed を描画する", async () => {
    server.use(
      http.get("/api/lots", () =>
        HttpResponse.json({
          items: [
            { status: "manufactured", lotNumber: "2026-A-1", version: 1 },
            { status: "manufactured", lotNumber: "2026-A-2", version: 2 },
            { status: "manufacturing", lotNumber: "2026-A-3", version: 1 },
          ],
          total: 3,
          limit: 100,
          offset: 0,
        }),
      ),
      http.get("/api/sales-cases", () =>
        HttpResponse.json({
          items: [{ salesCaseNumber: "2026-1-1", caseType: "direct", status: "before_appraisal" }],
          total: 1,
          limit: 100,
          offset: 0,
        }),
      ),
    );
    renderWithRealRouter("/");
    expect(await screen.findByRole("heading", { name: "ダッシュボード" })).toBeInTheDocument();
    // KPI: 在庫ロット総数 3 / 製造完了・出荷待ち 2 / 査定待ち 1 / 案件総数 1
    expect(screen.getByText("在庫ロット総数").closest(".kpi")!).toHaveTextContent("3");
    expect(screen.getByText("製造完了・出荷待ち").closest(".kpi")!).toHaveTextContent("2");
    expect(screen.getByText("査定待ち案件").closest(".kpi")!).toHaveTextContent("1");
    // 直近ロット feed (と他カード) に lotNumber リンク
    expect((await screen.findAllByRole("link", { name: "2026-A-1" })).length).toBeGreaterThan(0);
    // 状態別内訳バー
    expect(screen.getByRole("img", { name: "状態内訳" })).toBeInTheDocument();
  });

  it("items 空のとき EmptyState を描画する", async () => {
    server.use(
      http.get("/api/lots", () =>
        HttpResponse.json({ items: [], total: 0, limit: 100, offset: 0 }),
      ),
      http.get("/api/sales-cases", () =>
        HttpResponse.json({ items: [], total: 0, limit: 100, offset: 0 }),
      ),
    );
    renderWithRealRouter("/");
    expect(await screen.findByText("ロットがありません")).toBeInTheDocument();
  });
});
