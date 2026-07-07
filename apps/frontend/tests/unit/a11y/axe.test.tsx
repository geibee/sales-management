/**
 * jest-axe による a11y 自動検査 (issue #9 Tier2-16)。
 *
 * 手書きの aria assertion ではルール網羅が不可能なため、axe-core のルール
 * エンジンで主要ページの描画結果を機械検査する。ここで対象にするのは
 * 「データが揃った成功状態」— loading / error 状態の a11y は各ページの
 * 個別テストが担う。
 */
import { LotCreatePage } from "@/pages/lots/LotCreatePage";
import { LotListPage } from "@/pages/lots/LotListPage";
import { SalesCaseListPage } from "@/pages/sales-cases/SalesCaseListPage";
import { waitFor } from "@testing-library/react";
import { axe } from "jest-axe";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { makeAvailableLotsResponse, makeCodeMasters } from "../../support/fixtures";
import { renderWithRouter } from "../../support/render";
import { server } from "../../support/server";

function mockListEndpoints() {
  server.use(
    // LotCreatePage は Guard(operator) 配下のため auth OFF で children を描画させる
    http.get("/api/auth/config", () => HttpResponse.json({ enabled: false })),
    http.get("/api/lots", () =>
      HttpResponse.json({
        items: [
          {
            lotNumber: "2026-A-001",
            status: "manufactured",
            version: 1,
            manufacturingCompletedDate: "2026-04-01",
          },
        ],
        total: 1,
        limit: 50,
        offset: 0,
      }),
    ),
    http.get("/api/lots/available", () => HttpResponse.json(makeAvailableLotsResponse())),
    http.get("/api/sales-cases", () =>
      HttpResponse.json({
        items: [
          {
            salesCaseNumber: "2026-01-001",
            divisionCode: 10,
            salesDate: "2026-05-01",
            caseType: "direct",
            status: "before_appraisal",
          },
        ],
        total: 1,
        limit: 50,
        offset: 0,
      }),
    ),
    http.get("/api/code-masters", () => HttpResponse.json(makeCodeMasters())),
  );
}

async function expectNoAxeViolations(container: HTMLElement) {
  const results = await axe(container);
  expect(results.violations).toEqual([]);
}

describe("a11y (axe-core)", () => {
  it("LotListPage に axe 違反がない", async () => {
    mockListEndpoints();
    const { container, findByText } = renderWithRouter(<LotListPage />);
    await findByText("2026-A-001");
    await expectNoAxeViolations(container);
  });

  it("LotCreatePage に axe 違反がない", async () => {
    mockListEndpoints();
    const { container, findByText } = renderWithRouter(<LotCreatePage />);
    await waitFor(async () => {
      await findByText(/在庫ロット/);
    });
    await expectNoAxeViolations(container);
  });

  it("SalesCaseListPage に axe 違反がない", async () => {
    mockListEndpoints();
    const { container, findByText } = renderWithRouter(<SalesCaseListPage />);
    await findByText("2026-01-001");
    await expectNoAxeViolations(container);
  });
});
