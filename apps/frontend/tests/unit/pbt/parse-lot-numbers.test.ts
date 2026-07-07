/**
 * FE-PBT-SALES-LOTS-001 — parseLotNumbers (sales-case-create-validation):
 * 区切り (改行・カンマ) と空白をどう混ぜても、空要素を含まず trim 済みの
 * 配列が返り、有効 ID 列は round-trip で保存される。
 */
import { parseLotNumbers } from "@/pages/sales-cases/sales-case-create-validation";
import fc from "fast-check";
import { describe, expect, it } from "vitest";
import { pbtOpts, pbtTimeout } from "./pbt-env";

const lotIdArb = fc.stringMatching(/^\d{4}-[A-Z0-9]{1,4}-\d{3}$/);

describe("parseLotNumbers (PBT)", () => {
  it(
    "FE-PBT-SALES-LOTS-001: 有効 ID 列は区切り方によらず round-trip する",
    () => {
      const sepArb = fc.constantFrom("\n", ",", ",\n", " ,", "\n ");
      fc.assert(
        fc.property(fc.uniqueArray(lotIdArb, { maxLength: 8 }), sepArb, (ids, sep) => {
          expect(parseLotNumbers(ids.join(sep))).toEqual(ids);
        }),
        pbtOpts,
      );
    },
    pbtTimeout,
  );

  it(
    "FE-PBT-SALES-LOTS-001: 出力は空文字を含まず全要素 trim 済み",
    () => {
      fc.assert(
        fc.property(fc.string(), (raw) => {
          const parsed = parseLotNumbers(raw);
          for (const item of parsed) {
            expect(item).not.toBe("");
            expect(item).toBe(item.trim());
            expect(item).not.toMatch(/[\n,]/);
          }
        }),
        pbtOpts,
      );
    },
    pbtTimeout,
  );
});
