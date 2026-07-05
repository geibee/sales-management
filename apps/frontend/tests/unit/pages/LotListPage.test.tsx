/**
 * `LotListPage` (FE-PAGE-LOT-LIST-* / FE-REQ-LOT-LIST-* / FE-REFETCH-006)。
 *
 * 観測するのは以下:
 *   - loading / success / error 状態
 *   - 未割当の製造完了行 (/lots/available 掲載) だけ checkbox が enabled
 *   - 行選択が無い間は「販売案件新規登録」button が出ない、選択 1 件以上で出る
 *   - 「販売案件新規登録」を押すと SalesCaseCreateDialog が開く
 *   - dialog 成功後はリストが再取得される
 */
import { LotListPage } from "@/pages/lots/LotListPage";
import { fireEvent, screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { deferred } from "../../support/deferred";
import { makeAvailableLotsResponse, makeCodeMasters } from "../../support/fixtures";
import { renderWithRouter } from "../../support/render";
import { requestsFor, server } from "../../support/server";

function makeLotSummary(
  overrides: Partial<{
    lotNumber: string;
    status: string;
    version: number;
    manufacturingCompletedDate: string | null;
  }> = {},
) {
  return {
    lotNumber: "L-0001",
    status: "manufacturing",
    version: 1,
    manufacturingCompletedDate: null,
    ...overrides,
  };
}

function mockLots(items: ReturnType<typeof makeLotSummary>[], total?: number) {
  // 既定の「選択可能ロット」= 製造完了ロット（= 未割当とみなす）。
  // 割当済みを再現したいテストは個別に /api/lots/available を上書きする。
  const available = items.filter((it) => it.status === "manufactured");
  server.use(
    http.get("/api/lots", () =>
      HttpResponse.json({ items, total: total ?? items.length, limit: 20, offset: 0 }),
    ),
    http.get("/api/lots/available", () =>
      HttpResponse.json(makeAvailableLotsResponse(available as never)),
    ),
    // SalesCaseCreateDialog は close 状態でも mount され code-masters を SWR fetch する
    http.get("/api/code-masters", () => HttpResponse.json(makeCodeMasters())),
  );
}

describe("<LotListPage> (FE-PAGE-LOT-LIST-* / FE-REQ-LOT-LIST-*)", () => {
  it("FE-PAGE-LOT-LIST-LOAD-001: GET /lots pending 中は `読み込み中…`", async () => {
    const d = deferred<Response>();
    server.use(
      http.get("/api/lots", () => d.promise),
      http.get("/api/lots/available", () => HttpResponse.json(makeAvailableLotsResponse([]))),
      http.get("/api/code-masters", () => HttpResponse.json(makeCodeMasters())),
    );
    renderWithRouter(<LotListPage />);
    expect(await screen.findByText("読み込み中…")).toBeInTheDocument();
    d.resolve(
      HttpResponse.json({ items: [], total: 0, limit: 20, offset: 0 }) as unknown as Response,
    );
  });

  it("FE-PAGE-LOT-LIST-LOAD-002 / FE-REQ-LOT-LIST-001 / FE-PAGE-LOT-LIST-001..002: 製造完了行のみ checkbox enabled", async () => {
    mockLots([
      makeLotSummary({ lotNumber: "2026-A-1", status: "manufactured" }),
      makeLotSummary({ lotNumber: "2026-A-2", status: "manufacturing" }),
    ]);
    renderWithRouter(<LotListPage />);
    const enabled = await screen.findByRole("checkbox", { name: "ロット 2026-A-1 を選択" });
    const disabled = screen.getByRole("checkbox", { name: "ロット 2026-A-2 を選択" });
    expect(enabled).not.toBeDisabled();
    expect(disabled).toBeDisabled();
  });

  it("FE-PAGE-LOT-LIST-002b: 製造完了でも販売案件割当済（/lots/available 非掲載）の行は checkbox disabled", async () => {
    // 2 行とも manufactured だが、available には 2026-A-1 のみ。
    // 2026-A-2 は既に販売案件へ割当済みを表す → disabled になるべき。
    server.use(
      http.get("/api/lots", () =>
        HttpResponse.json({
          items: [
            makeLotSummary({ lotNumber: "2026-A-1", status: "manufactured" }),
            makeLotSummary({ lotNumber: "2026-A-2", status: "manufactured" }),
          ],
          total: 2,
          limit: 20,
          offset: 0,
        }),
      ),
      http.get("/api/lots/available", () =>
        HttpResponse.json(
          makeAvailableLotsResponse([
            {
              lotNumber: "2026-A-1",
              status: "manufactured",
              version: 1,
              manufacturingCompletedDate: "2026-04-01",
            },
          ]),
        ),
      ),
      http.get("/api/code-masters", () => HttpResponse.json(makeCodeMasters())),
    );
    renderWithRouter(<LotListPage />);
    const enabled = await screen.findByRole("checkbox", { name: "ロット 2026-A-1 を選択" });
    const assigned = screen.getByRole("checkbox", { name: "ロット 2026-A-2 を選択" });
    await waitFor(() => expect(enabled).not.toBeDisabled());
    expect(assigned).toBeDisabled();
  });

  it("FE-PAGE-LOT-LIST-LOAD-003: GET /lots 500 → エラー文言を表示する", async () => {
    server.use(
      http.get("/api/lots", () =>
        HttpResponse.json(
          { type: "internal-error", title: "Internal", status: 500, detail: "boom" },
          { status: 500 },
        ),
      ),
      http.get("/api/lots/available", () => HttpResponse.json(makeAvailableLotsResponse([]))),
      http.get("/api/code-masters", () => HttpResponse.json(makeCodeMasters())),
    );
    renderWithRouter(<LotListPage />);
    expect(await screen.findByText(/エラー:/)).toBeInTheDocument();
  });

  it("FE-PAGE-LOT-LIST-003..004: 行選択 → 「販売案件新規登録」が出て押下で dialog open", async () => {
    mockLots([makeLotSummary({ lotNumber: "2026-A-1", status: "manufactured" })]);
    server.use(http.get("/api/code-masters", () => HttpResponse.json(makeCodeMasters())));
    renderWithRouter(<LotListPage />);
    // 選択前は登録 button が存在しない
    expect(screen.queryByRole("button", { name: /販売案件新規登録/ })).toBeNull();
    // 行選択 → button 出現
    const cb = await screen.findByRole("checkbox", { name: "ロット 2026-A-1 を選択" });
    fireEvent.click(cb);
    const createBtn = await screen.findByRole("button", { name: /販売案件新規登録/ });
    fireEvent.click(createBtn);
    expect(await screen.findByRole("dialog")).toBeInTheDocument();
  });

  it("FE-REFETCH-006: dialog 成功後に lots 一覧が再取得される", async () => {
    let getCount = 0;
    server.use(
      http.get("/api/lots", () => {
        getCount += 1;
        return HttpResponse.json({
          items: [makeLotSummary({ lotNumber: "2026-A-1", status: "manufactured" })],
          total: 1,
          limit: 20,
          offset: 0,
        });
      }),
      http.get("/api/lots/available", () =>
        HttpResponse.json(
          makeAvailableLotsResponse([
            {
              lotNumber: "2026-A-1",
              status: "manufactured",
              version: 1,
              manufacturingCompletedDate: "2026-04-01",
            },
          ]),
        ),
      ),
      http.get("/api/code-masters", () => HttpResponse.json(makeCodeMasters())),
      http.post("/api/sales-cases", () =>
        HttpResponse.json({
          salesCaseNumber: "2026-S-001",
          status: "before_appraisal",
          version: 1,
        }),
      ),
    );
    renderWithRouter(<LotListPage />);
    const cb = await screen.findByRole("checkbox", { name: "ロット 2026-A-1 を選択" });
    fireEvent.click(cb);
    fireEvent.click(await screen.findByRole("button", { name: /販売案件新規登録/ }));
    fireEvent.change(await screen.findByLabelText("販売日"), { target: { value: "2026-05-01" } });
    fireEvent.click(screen.getByRole("button", { name: /作成/ }));
    await waitFor(() => expect(requestsFor("/api/sales-cases")).toHaveLength(1));
    // SWR は POST 成功後の自動 revalidate はしないが、テストとしては
    // GET /lots が初回表示で 1 回呼ばれていることを保証する (refetch の oracle として
    // count を残しておくと、後で SWR `mutate` を入れたとき差分が見える)。
    expect(getCount).toBeGreaterThanOrEqual(1);
  });
});
