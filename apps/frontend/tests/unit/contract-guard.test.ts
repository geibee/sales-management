/**
 * contract-guard (MSW ↔ zod ドリフト検査) 自体の動作検証。
 * ゲートを検証するゲート — ガードが壊れて fail-open になるリグレッションを防ぐ。
 */
import { HttpResponse, http } from "msw";
import { describe, expect, it } from "vitest";
import { assertNoContractViolations } from "../support/contract-guard";
import { server } from "../support/server";

describe("contract-guard (MSW ↔ zod ドリフト検査)", () => {
  it("契約に適合しないモックレスポンスを違反として検出する", async () => {
    // LotResponse は status / version / details 等が必須。欠落モックは契約ドリフト
    server.use(http.get("/api/lots/:id", () => HttpResponse.json({ lotNumber: 123 })));
    await fetch("/api/lots/L-0001");
    await expect(assertNoContractViolations()).rejects.toThrow("contract-guard");
  });

  it("契約に適合するモックは違反にしない", async () => {
    server.use(
      http.get("/api/lots", () => HttpResponse.json({ items: [], total: 0, limit: 20, offset: 0 })),
    );
    await fetch("/api/lots");
    await assertNoContractViolations();
  });

  it("契約外 path のモックは対象外 (テスト専用モックを許容)", async () => {
    server.use(http.get("/api/not-in-contract", () => HttpResponse.json({ anything: true })));
    await fetch("/api/not-in-contract");
    await assertNoContractViolations();
  });

  it("spec 宣言済みエラー status は Problem Details 形状を要求する", async () => {
    server.use(http.get("/api/lots/:id", () => HttpResponse.json({}, { status: 404 })));
    await fetch("/api/lots/L-0001");
    await expect(assertNoContractViolations()).rejects.toThrow("contract-guard");
  });
});
