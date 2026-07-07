/**
 * `PriceCheckPage` (FE-PAGE-PRICE-* / FE-REQ-PRICE-* / FE-REFETCH-004)。
 *
 * 外部価格 API はステータスコードごとに UI 文言が分かれる。
 * - idle: 取得 button disabled
 * - 200 success: 結果表示
 * - 200 with null adjustmentRate: `(未設定)` 表示
 * - 400 / 502 / 503 / network: それぞれ専用文言
 * - 同 lotId で連続押下: SWR dedupe を無効化済みなので request 数が増える
 * - 異なる lotId で押下: 新規 query
 */
import { PriceCheckPage } from "@/pages/external/PriceCheckPage";
import { fireEvent, screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { makePriceQuote, makeProblem } from "../../support/fixtures";
import { renderWithApp } from "../../support/render";
import { requestsFor, server } from "../../support/server";

function input(value: string): HTMLInputElement {
  const lotId = screen.getByLabelText("ロット番号") as HTMLInputElement;
  fireEvent.change(lotId, { target: { value } });
  return lotId;
}

function fetchButton(): HTMLButtonElement {
  return screen.getByRole("button", { name: /取得/ }) as HTMLButtonElement;
}

describe("<PriceCheckPage> (FE-PAGE-PRICE-* / FE-REQ-PRICE-*)", () => {
  it("FE-PAGE-PRICE-001: 初期表示 (lotId 空) → `取得` disabled", () => {
    renderWithApp(<PriceCheckPage />);
    expect(fetchButton()).toBeDisabled();
  });

  it("FE-PAGE-PRICE-002: 空白のみ入力 → `取得` disabled", () => {
    renderWithApp(<PriceCheckPage />);
    input("   ");
    expect(fetchButton()).toBeDisabled();
  });

  it("FE-PAGE-PRICE-003 / FE-REQ-PRICE-001: 200 success → 結果を表示し、query に lotId が乗る", async () => {
    server.use(
      http.get("/api/external/price-check", () =>
        HttpResponse.json(makePriceQuote({ basePrice: 1234, adjustmentRate: 0.95, source: "x" })),
      ),
    );
    renderWithApp(<PriceCheckPage />);
    input("2026-A-001");
    fireEvent.click(fetchButton());
    await screen.findByText("取得結果");
    expect(screen.getByText("1234")).toBeInTheDocument();
    expect(screen.getByText("0.95")).toBeInTheDocument();
    expect(screen.getByText("x")).toBeInTheDocument();
    const reqs = requestsFor("/api/external/price-check");
    expect(reqs).toHaveLength(1);
    expect(reqs[0]!.search).toContain("lotId=2026-A-001");
  });

  it("FE-PAGE-PRICE-004: adjustmentRate=null → `(未設定)` 表示", async () => {
    server.use(
      http.get("/api/external/price-check", () =>
        HttpResponse.json(makePriceQuote({ adjustmentRate: null })),
      ),
    );
    renderWithApp(<PriceCheckPage />);
    input("2026-A-001");
    fireEvent.click(fetchButton());
    await screen.findByText("取得結果");
    expect(screen.getByText("(未設定)")).toBeInTheDocument();
  });

  it("FE-PAGE-PRICE-005: 400 problem → `ロット番号の形式が不正です。`", async () => {
    server.use(
      http.get("/api/external/price-check", () =>
        HttpResponse.json(makeProblem({ status: 400, detail: "invalid lot id" }), { status: 400 }),
      ),
    );
    renderWithApp(<PriceCheckPage />);
    input("BAD");
    fireEvent.click(fetchButton());
    expect(await screen.findByText("ロット番号の形式が不正です。")).toBeInTheDocument();
  });

  it("FE-PAGE-PRICE-006: 502 problem → `上流の価格 API がエラーを返しました。`", async () => {
    server.use(
      http.get("/api/external/price-check", () =>
        HttpResponse.json(makeProblem({ status: 502, detail: "upstream down" }), { status: 502 }),
      ),
    );
    renderWithApp(<PriceCheckPage />);
    input("2026-A-001");
    fireEvent.click(fetchButton());
    expect(await screen.findByText("上流の価格 API がエラーを返しました。")).toBeInTheDocument();
  });

  it("FE-PAGE-PRICE-007: 503 problem → `サーキットが OPEN しています...`", async () => {
    server.use(
      http.get("/api/external/price-check", () =>
        HttpResponse.json(makeProblem({ status: 503, detail: "circuit open" }), { status: 503 }),
      ),
    );
    renderWithApp(<PriceCheckPage />);
    input("2026-A-001");
    fireEvent.click(fetchButton());
    expect(await screen.findByText(/サーキットが OPEN しています/)).toBeInTheDocument();
  });

  it("FE-PAGE-PRICE-008: unknown / network → `取得に失敗しました。`", async () => {
    server.use(
      http.get("/api/external/price-check", () =>
        HttpResponse.json(makeProblem({ status: 500, detail: "boom" }), { status: 500 }),
      ),
    );
    renderWithApp(<PriceCheckPage />);
    input("2026-A-001");
    fireEvent.click(fetchButton());
    expect(await screen.findByText("取得に失敗しました。")).toBeInTheDocument();
  });

  it("FE-REQ-PRICE-002 / FE-REFETCH-004: 同じ lotId で再押下 → request 数が増える (dedupe 無効)", async () => {
    server.use(
      http.get("/api/external/price-check", () =>
        HttpResponse.json(makePriceQuote({ basePrice: 100 })),
      ),
    );
    renderWithApp(<PriceCheckPage />);
    input("2026-A-001");
    fireEvent.click(fetchButton());
    await screen.findByText("取得結果");
    expect(requestsFor("/api/external/price-check")).toHaveLength(1);
    fireEvent.click(fetchButton());
    await waitFor(() =>
      expect(requestsFor("/api/external/price-check").length).toBeGreaterThanOrEqual(2),
    );
  });

  it("FE-REQ-PRICE-003: 異なる lotId に変えて押下 → 新 query で request", async () => {
    server.use(
      http.get("/api/external/price-check", ({ request }) =>
        HttpResponse.json(
          makePriceQuote({
            basePrice: new URL(request.url).searchParams.get("lotId") === "X-1" ? 100 : 200,
          }),
        ),
      ),
    );
    renderWithApp(<PriceCheckPage />);
    input("X-1");
    fireEvent.click(fetchButton());
    await screen.findByText("取得結果");
    input("X-2");
    fireEvent.click(fetchButton());
    await waitFor(() => {
      const last = requestsFor("/api/external/price-check").at(-1);
      expect(last?.search).toContain("lotId=X-2");
    });
  });
});
