/**
 * FE-PBT-TOTAL-001 — computeEstimatedTotal (lib/rate) が手計算の oracle と一致する。
 */
import { type AppraisalRateRow, computeEstimatedTotal } from "@/lib/rate";
import fc from "fast-check";
import { describe, expect, it } from "vitest";
import { pbtOpts, pbtTimeout } from "./pbt-env";

const rowArb: fc.Arbitrary<AppraisalRateRow> = fc.record({
  base: fc.integer({ min: 0, max: 1_000_000 }),
  period: fc.integer({ min: 90, max: 110 }),
  counterparty: fc.integer({ min: 90, max: 110 }),
  exceptional: fc.oneof(fc.constant(null), fc.integer({ min: 90, max: 110 })),
});

function oracle(rows: AppraisalRateRow[]): number {
  const total = rows.reduce((acc, row) => {
    const exceptional = row.exceptional === null ? 100 : row.exceptional;
    return acc + (row.base * row.period * row.counterparty * exceptional) / 1_000_000;
  }, 0);
  return Math.round(total);
}

describe("lib/rate computeEstimatedTotal (PBT)", () => {
  it(
    "FE-PBT-TOTAL-001: Math.round(Σ base×period×counterparty×exceptional) が oracle と ±1 で一致",
    () => {
      fc.assert(
        fc.property(fc.array(rowArb, { maxLength: 10 }), (rows) => {
          expect(Math.abs(computeEstimatedTotal(rows) - oracle(rows))).toBeLessThanOrEqual(1);
        }),
        pbtOpts,
      );
    },
    pbtTimeout,
  );

  it("非数を含む行は合計から除外される", () => {
    const rows: AppraisalRateRow[] = [
      { base: 1000, period: 100, counterparty: 100, exceptional: null },
      { base: Number.NaN, period: 100, counterparty: 100, exceptional: null },
    ];
    expect(computeEstimatedTotal(rows)).toBe(1000);
  });
});
