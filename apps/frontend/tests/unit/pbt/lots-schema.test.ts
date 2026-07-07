/**
 * FE-PBT-LOTID-001 — lotsSchema (sales-case-create-validation):
 * 形式不正のロット ID が混ざったとき、エラーメッセージに全 invalid 値が列挙される
 * (最初の 1 件で打ち切らない = ユーザーが一括で修正できる)。
 */
import { lotsSchema } from "@/pages/sales-cases/sales-case-create-validation";
import fc from "fast-check";
import { describe, expect, it } from "vitest";
import { pbtOpts, pbtTimeout } from "./pbt-env";

const LOT_NUMBER_PATTERN = /^\d+-[^-\s]+-\d+$/;

const validIdArb = fc.stringMatching(/^\d{4}-[A-Z0-9]{1,4}-\d{3}$/);

const invalidIdArb = fc
  .string({ minLength: 1 })
  .filter((s) => !LOT_NUMBER_PATTERN.test(s) && s.trim() !== "");

describe("lotsSchema (PBT)", () => {
  it(
    "FE-PBT-LOTID-001: invalid が混ざると issue に全 invalid が列挙される",
    () => {
      fc.assert(
        fc.property(
          fc.array(fc.oneof(validIdArb, invalidIdArb), { minLength: 1, maxLength: 8 }),
          (lots) => {
            const invalid = lots.filter((lot) => !LOT_NUMBER_PATTERN.test(lot));
            const result = lotsSchema.safeParse(lots);

            if (invalid.length === 0) {
              expect(result.success).toBe(true);
            } else {
              expect(result.success).toBe(false);
              if (!result.success) {
                const message = result.error.issues.map((i) => i.message).join("\n");
                for (const bad of invalid) {
                  expect(message).toContain(bad);
                }
              }
            }
          },
        ),
        pbtOpts,
      );
    },
    pbtTimeout,
  );

  it("空配列は min(1) で reject される", () => {
    expect(lotsSchema.safeParse([]).success).toBe(false);
  });
});
