/**
 * FE-PBT-FORMAT-001 — lotStatusLabel / caseStatusLabel (lib/format):
 * 未知 status は入力値をそのまま fallback 表示する (握り潰して "(unknown)" 化しない)。
 */
import {
  CONSIGNMENT_STATUS_LABEL,
  ESTIMATE_STATUS_LABEL,
  LOT_STATUS_LABEL,
  SALES_CASE_STATUS_LABEL,
  caseStatusLabel,
  lotStatusLabel,
} from "@/lib/format";
import fc from "fast-check";
import { describe, expect, it } from "vitest";
import { pbtOpts, pbtTimeout } from "./pbt-env";

const KNOWN_LOT = Object.keys(LOT_STATUS_LABEL);

const KNOWN_CASE = [
  ...Object.keys(SALES_CASE_STATUS_LABEL),
  ...Object.keys(ESTIMATE_STATUS_LABEL),
  ...Object.keys(CONSIGNMENT_STATUS_LABEL),
];

describe("lib/format status labels (PBT)", () => {
  it(
    "FE-PBT-FORMAT-001: 未知 lot status は入力値を fallback 表示",
    () => {
      const unknownArb = fc
        .string({ minLength: 1 })
        .filter((s) => !KNOWN_LOT.includes(s));
      fc.assert(
        fc.property(unknownArb, (status) => {
          expect(lotStatusLabel(status)).toBe(status);
        }),
        pbtOpts,
      );
    },
    pbtTimeout,
  );

  it(
    "FE-PBT-FORMAT-001: 未知 case status は caseType によらず入力値を fallback 表示",
    () => {
      const unknownArb = fc
        .string({ minLength: 1 })
        .filter((s) => !KNOWN_CASE.includes(s));
      const caseTypeArb = fc.constantFrom("direct", "reservation", "consignment", "other", null);
      fc.assert(
        fc.property(caseTypeArb, unknownArb, (caseType, status) => {
          expect(caseStatusLabel(caseType, status)).toBe(status);
        }),
        pbtOpts,
      );
    },
    pbtTimeout,
  );

  it("null / undefined / 空文字は '(unknown)'", () => {
    expect(lotStatusLabel(null)).toBe("(unknown)");
    expect(lotStatusLabel(undefined)).toBe("(unknown)");
    expect(lotStatusLabel("")).toBe("(unknown)");
    expect(caseStatusLabel("direct", null)).toBe("(unknown)");
  });
});
