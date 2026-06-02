/**
 * `SalesCaseListPage`（計画 P1-2 — List 系で唯一テスト欠落だったページ）。
 *
 * 観測するのは刷新で動いた分岐:
 *   - loading / empty / error の各状態
 *   - 種別チップ (direct/reservation/consignment) と状態 select が
 *     GET /sales-cases のクエリ (caseType / status) に反映される
 *   - 行クリックの「多態ルーティング」: caseType により詳細リンクの href が
 *     /sales-cases/$id・/reservation-cases/$id・/consignment-cases/$id に変わる
 *   - ページング (前へ/次へ) で offset が進み、端で disabled になる
 *
 * パターンは `LotListPage.test.tsx` / `SalesCaseDetailPage.test.tsx` を踏襲。
 */
import { SalesCaseListPage } from "@/pages/sales-cases/SalesCaseListPage";
import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { deferred } from "../../support/deferred";
import { renderWithRouter } from "../../support/render";
import { requestsFor, server } from "../../support/server";

type CaseType = "direct" | "reservation" | "consignment";

function makeCaseSummary(
  overrides: Partial<{
    salesCaseNumber: string;
    caseType: CaseType;
    status: string;
    salesDate: string | null;
  }> = {},
) {
  return {
    salesCaseNumber: "2026-S-001",
    caseType: "direct" as CaseType,
    status: "before_appraisal",
    salesDate: null,
    ...overrides,
  };
}

/** GET /sales-cases を固定レスポンスでモックする。total 省略時は items 数。 */
function mockCases(items: ReturnType<typeof makeCaseSummary>[], total?: number) {
  server.use(
    http.get("/api/sales-cases", () =>
      HttpResponse.json({ items, total: total ?? items.length, limit: 20, offset: 0 }),
    ),
  );
}

/** 直近の GET /sales-cases の URLSearchParams を返す。 */
function lastQuery(): URLSearchParams {
  const reqs = requestsFor("/api/sales-cases");
  return new URL(reqs[reqs.length - 1].url).searchParams;
}

describe("<SalesCaseListPage>", () => {
  it("読み込み中: GET pending の間は `読み込み中…`", async () => {
    const d = deferred<Response>();
    server.use(http.get("/api/sales-cases", () => d.promise));
    renderWithRouter(<SalesCaseListPage />);
    expect(await screen.findByText("読み込み中…")).toBeInTheDocument();
    d.resolve(
      HttpResponse.json({ items: [], total: 0, limit: 20, offset: 0 }) as unknown as Response,
    );
  });

  it("空: items 0 件は EmptyState を表示", async () => {
    mockCases([]);
    renderWithRouter(<SalesCaseListPage />);
    expect(await screen.findByText("該当する案件がありません")).toBeInTheDocument();
  });

  it("エラー: GET 500 → エラー文言を表示", async () => {
    server.use(
      http.get("/api/sales-cases", () =>
        HttpResponse.json(
          { type: "internal-error", title: "Internal", status: 500, detail: "boom" },
          { status: 500 },
        ),
      ),
    );
    renderWithRouter(<SalesCaseListPage />);
    expect(await screen.findByText(/エラー:/)).toBeInTheDocument();
  });

  it("行描画: 案件番号・種別・状態ラベルが表示される", async () => {
    mockCases([
      makeCaseSummary({
        salesCaseNumber: "2026-S-010",
        caseType: "direct",
        status: "appraised",
        salesDate: "2026-05-01",
      }),
    ]);
    renderWithRouter(<SalesCaseListPage />);
    // チップ/状態 select にも同じラベルが出るため、行 (td) 内に限定して検証する。
    const cell = (await screen.findByText("2026-S-010")).closest("td") as HTMLElement;
    const row = cell.closest("tr") as HTMLElement;
    expect(within(row).getByText("直接販売")).toBeInTheDocument();
    expect(within(row).getByText("査定済")).toBeInTheDocument();
    expect(within(row).getByText("2026-05-01")).toBeInTheDocument();
  });

  it("多態ルーティング: caseType ごとに詳細リンクの href が切り替わる", async () => {
    mockCases([
      makeCaseSummary({ salesCaseNumber: "D-1", caseType: "direct" }),
      makeCaseSummary({ salesCaseNumber: "R-1", caseType: "reservation", status: "reserved" }),
      makeCaseSummary({
        salesCaseNumber: "C-1",
        caseType: "consignment",
        status: "before_consignment",
      }),
    ]);
    renderWithRouter(<SalesCaseListPage />);

    const direct = await screen.findByRole("link", { name: "D-1" });
    expect(direct).toHaveAttribute("href", "/sales-cases/D-1");
    expect(screen.getByRole("link", { name: "R-1" })).toHaveAttribute(
      "href",
      "/reservation-cases/R-1",
    );
    expect(screen.getByRole("link", { name: "C-1" })).toHaveAttribute(
      "href",
      "/consignment-cases/C-1",
    );

    // 行末の詳細アイコンリンクも同じ多態先を指す。
    expect(screen.getByRole("link", { name: "案件 R-1 の詳細" })).toHaveAttribute(
      "href",
      "/reservation-cases/R-1",
    );
  });

  it("種別フィルタ: チップ押下で caseType クエリが付き offset がリセットされる", async () => {
    mockCases([makeCaseSummary()]);
    renderWithRouter(<SalesCaseListPage />);
    await screen.findByText("2026-S-001");

    // 初回 (all) は caseType を送らない。
    expect(lastQuery().has("caseType")).toBe(false);

    fireEvent.click(screen.getByRole("button", { name: "予約" }));
    await waitFor(() => expect(lastQuery().get("caseType")).toBe("reservation"));
    expect(lastQuery().get("offset")).toBe("0");
  });

  it("状態フィルタ: select 変更で status クエリが付く", async () => {
    mockCases([makeCaseSummary()]);
    renderWithRouter(<SalesCaseListPage />);
    await screen.findByText("2026-S-001");

    fireEvent.change(screen.getByLabelText("状態フィルタ"), {
      target: { value: "contracted" },
    });
    await waitFor(() => expect(lastQuery().get("status")).toBe("contracted"));
  });

  it("ページング: 次へで offset が PAGE_SIZE 進み、先頭では前へが無効", async () => {
    // total 25 → 2 ページ。1 ページ目では「前へ」disabled・「次へ」enabled。
    mockCases([makeCaseSummary({ salesCaseNumber: "2026-S-001" })], 25);
    renderWithRouter(<SalesCaseListPage />);
    await screen.findByText("2026-S-001");

    const prev = screen.getByRole("button", { name: /前へ/ });
    const next = screen.getByRole("button", { name: /次へ/ });
    expect(prev).toBeDisabled();
    expect(next).not.toBeDisabled();

    fireEvent.click(next);
    await waitFor(() => expect(lastQuery().get("offset")).toBe("20"));
  });
});
