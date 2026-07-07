/**
 * FE-PBT-RATE-001 / FE-PBT-RATE-002 — 調整率変換と range guard (lib/rate)。
 */
import {
  RATE_DISPLAY_MAX,
  RATE_DISPLAY_MIN,
  apiToDisplayRate,
  displayToApiRate,
  isRateDisplayInRange,
} from "@/lib/rate";
import fc from "fast-check";
import { describe, expect, it } from "vitest";
import { pbtOpts, pbtTimeout } from "./pbt-env";

describe("lib/rate (PBT)", () => {
  it(
    "FE-PBT-RATE-001: 画面値 r∈[90,110] の API 値は [0.9,1.1]、逆変換で誤差 <1e-9",
    () => {
      fc.assert(
        fc.property(fc.integer({ min: RATE_DISPLAY_MIN, max: RATE_DISPLAY_MAX }), (display) => {
          const api = displayToApiRate(display);
          expect(api).toBeGreaterThanOrEqual(0.9);
          expect(api).toBeLessThanOrEqual(1.1);
          expect(Math.abs(apiToDisplayRate(api) - display)).toBeLessThan(1e-9);
        }),
        pbtOpts,
      );
    },
    pbtTimeout,
  );

  it(
    "FE-PBT-RATE-002: range guard は [90,110] のみ valid",
    () => {
      fc.assert(
        fc.property(fc.integer({ min: -1000, max: 1000 }), (r) => {
          expect(isRateDisplayInRange(r)).toBe(r >= RATE_DISPLAY_MIN && r <= RATE_DISPLAY_MAX);
        }),
        pbtOpts,
      );
    },
    pbtTimeout,
  );

  it("FE-PBT-RATE-002 境界: 非数は valid にならない", () => {
    for (const v of [Number.NaN, Number.POSITIVE_INFINITY, Number.NEGATIVE_INFINITY]) {
      expect(isRateDisplayInRange(v)).toBe(false);
    }
  });
});
