import { describe, expect, it } from "vitest";
import {
  DateOnlySchema,
  LotResponseSchema,
  LotStatusSchema,
  PriceCheckResponseSchema,
  ProblemJsonSchema,
  SalesCaseDetailResponseSchema,
} from "@/contracts";

describe("contracts", () => {
  it("LotResponseSchema accepts a typical 200 body (lotNumber as string + version)", () => {
    const ok = LotResponseSchema.parse({
      lotNumber: "2026-A-001",
      status: "manufacturing",
      version: 1,
      manufacturingCompletedDate: null,
      shippingDeadlineDate: null,
      shippedDate: null,
      destinationItem: null,
    });
    expect(ok.lotNumber).toBe("2026-A-001");
    expect(ok.version).toBe(1);
  });

  it("LotResponseSchema passes through unknown fields", () => {
    const result = LotResponseSchema.parse({
      lotNumber: "2026-A-1",
      status: "manufactured",
      version: 2,
      auditCreatedBy: "user-1",
    });
    expect((result as { auditCreatedBy?: string }).auditCreatedBy).toBe("user-1");
  });

  it("LotStatusSchema accepts the snake_case values", () => {
    expect(LotStatusSchema.parse("manufacturing")).toBe("manufacturing");
    expect(LotStatusSchema.parse("manufactured")).toBe("manufactured");
    expect(LotStatusSchema.parse("shipping_instructed")).toBe("shipping_instructed");
    expect(LotStatusSchema.parse("shipped")).toBe("shipped");
    expect(LotStatusSchema.parse("conversion_instructed")).toBe("conversion_instructed");
    expect(() => LotStatusSchema.parse("Random")).toThrow();
    expect(() => LotStatusSchema.parse("ManufacturingInProgress")).toThrow();
  });

  it("SalesCaseDetailResponseSchema accepts polymorphic detail body", () => {
    const direct = SalesCaseDetailResponseSchema.parse({
      salesCaseNumber: "2026-04-001",
      caseType: "direct",
      status: "appraised",
      lots: ["2026-A-001"],
      divisionCode: 1,
      salesDate: "2026-04-28",
      version: 1,
      appraisal: { foo: "bar" },
      contract: null,
    });
    expect(direct.caseType).toBe("direct");
    expect((direct as { appraisal?: unknown }).appraisal).toEqual({ foo: "bar" });
  });

  it("PriceCheckResponseSchema requires basePrice and source", () => {
    const ok = PriceCheckResponseSchema.parse({
      basePrice: 1500,
      adjustmentRate: 0.95,
      source: "wiremock",
    });
    expect(ok.basePrice).toBe(1500);
    expect(() => PriceCheckResponseSchema.parse({ basePrice: 1 })).toThrow();
  });

  it("ProblemJsonSchema requires status", () => {
    expect(() => ProblemJsonSchema.parse({ title: "Bad" })).toThrow();
    expect(ProblemJsonSchema.parse({ status: 400, title: "Bad" }).status).toBe(400);
  });

  it("DateOnlySchema enforces yyyy-MM-dd", () => {
    expect(DateOnlySchema.parse("2026-04-28")).toBe("2026-04-28");
    expect(() => DateOnlySchema.parse("2026/4/28")).toThrow();
    expect(() => DateOnlySchema.parse("28-04-2026")).toThrow();
  });
});
