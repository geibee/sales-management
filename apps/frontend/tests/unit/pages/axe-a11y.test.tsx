/**
 * axe-core によるページ横断の自動 a11y 監査 (WCAG 2.x ベースのルールセット)。
 *
 * form-a11y.test.tsx が個別のフォーム振る舞い (aria-invalid 等) を検査するのに
 * 対し、こちらは代表的なページアーキタイプ (一覧 / 作成フォーム / 単票) を
 * 描画して axe を一括実行し、既知パターン以外の違反ゼロを守るラチェット。
 *
 * jsdom 制約でスキップするルール:
 *   - color-contrast: canvas 実装がなく計測不能 (E2E/実ブラウザ側で検査する)
 *   - region: page コンポーネント単体描画では landmark (main 等) はレイアウト
 *     側の責務のため、単体では常に違反になる
 */
import { LotCreatePage } from "@/pages/lots/LotCreatePage";
import { LotListPage } from "@/pages/lots/LotListPage";
import { PriceCheckPage } from "@/pages/external/PriceCheckPage";
import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { axe } from "vitest-axe";
import { makeAvailableLotsResponse, makeCodeMasters } from "../../support/fixtures";
import { renderWithRouter } from "../../support/render";
import { server } from "../../support/server";

const AXE_OPTIONS = {
  rules: {
    "color-contrast": { enabled: false },
    region: { enabled: false },
  },
} as const;

function expectNoViolations(results: Awaited<ReturnType<typeof axe>>) {
  const summary = results.violations.map(
    (v) => `${v.id}: ${v.help} → ${v.nodes.map((n) => n.target.join(" ")).join(", ")}`,
  );
  expect(summary).toEqual([]);
}

function stubCommonApis() {
  server.use(
    http.get("/api/auth/config", () => HttpResponse.json({ enabled: false })),
    http.get("/api/code-masters", () => HttpResponse.json(makeCodeMasters())),
  );
}

describe("axe a11y 監査 (代表ページ)", () => {
  it("LotListPage (一覧): データ表示状態で違反ゼロ", async () => {
    stubCommonApis();
    server.use(
      http.get("/api/lots", () =>
        HttpResponse.json({
          items: [
            {
              lotNumber: "L-0001",
              status: "manufactured",
              version: 1,
              manufacturingCompletedDate: "2026-07-01",
            },
          ],
          total: 1,
          limit: 20,
          offset: 0,
        }),
      ),
      http.get("/api/lots/available", () => HttpResponse.json(makeAvailableLotsResponse())),
    );
    const { container } = renderWithRouter(<LotListPage />);
    await screen.findByText("L-0001");
    expectNoViolations(await axe(container, AXE_OPTIONS));
  });

  it("LotCreatePage (作成フォーム): 初期表示で違反ゼロ", async () => {
    stubCommonApis();
    const { container } = renderWithRouter(<LotCreatePage />);
    await screen.findByRole("button", { name: /作成/ });
    expectNoViolations(await axe(container, AXE_OPTIONS));
  });

  it("PriceCheckPage (単票): 初期表示で違反ゼロ", async () => {
    stubCommonApis();
    const { container } = renderWithRouter(<PriceCheckPage />);
    await screen.findByRole("button", { name: "取得" });
    expectNoViolations(await axe(container, AXE_OPTIONS));
  });
});
