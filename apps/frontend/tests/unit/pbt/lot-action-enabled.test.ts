/**
 * FE-PBT-STATUS-001 — lotActionEnabled (lib/format): 許可 matrix に無い
 * status × action の組は必ず false (未知 status を含む)。
 */
import { type LotAction, lotActionEnabled } from "@/lib/format";
import fc from "fast-check";
import { describe, expect, it } from "vitest";
import { pbtOpts, pbtTimeout } from "./pbt-env";

const LOT_ACTIONS: LotAction[] = [
  "complete-manufacturing",
  "cancel-manufacturing-completion",
  "instruct-shipping",
  "complete-shipping",
  "instruct-item-conversion",
  "cancel-item-conversion-instruction",
];

const KNOWN_STATUSES = [
  "manufacturing",
  "manufactured",
  "shipping_instructed",
  "shipped",
  "conversion_instructed",
];

/** バックエンド LotRoutes.fs の状態機械と対の許可 matrix (oracle)。 */
const ALLOWED: Record<LotAction, string> = {
  "complete-manufacturing": "manufacturing",
  "cancel-manufacturing-completion": "manufactured",
  "instruct-shipping": "manufactured",
  "complete-shipping": "shipping_instructed",
  "instruct-item-conversion": "manufactured",
  "cancel-item-conversion-instruction": "conversion_instructed",
};

describe("lib/format lotActionEnabled (PBT)", () => {
  it(
    "FE-PBT-STATUS-001: matrix に無い status × action は false",
    () => {
      const statusArb = fc.oneof(fc.constantFrom(...KNOWN_STATUSES), fc.string());
      fc.assert(
        fc.property(fc.constantFrom(...LOT_ACTIONS), statusArb, (action, status) => {
          expect(lotActionEnabled(action, status)).toBe(ALLOWED[action] === status);
        }),
        pbtOpts,
      );
    },
    pbtTimeout,
  );

  it("null / undefined の status では全 action が false", () => {
    for (const action of LOT_ACTIONS) {
      expect(lotActionEnabled(action, null)).toBe(false);
      expect(lotActionEnabled(action, undefined)).toBe(false);
    }
  });
});
