/**
 * `src/lib/format.ts` の property-based テスト (fast-check)。
 *
 * format.test.ts が既知の代表値・全真理値表を固定するのに対し、こちらは
 * ランダム入力で「どんな値でも成り立つ不変条件」を検査する:
 *   - 桁区切りは可逆 (カンマを剥がせば元の数値に戻る)
 *   - 数量は小数最大 3 桁・丸め誤差が保証内
 *   - 未知の status では状態遷移アクションが 1 つも有効にならない
 *     (バックエンド状態機械の写しが「知らない状態で誤って活性化」しないこと)
 *   - 未知ラベルはフォールバックとして入力をそのまま返す
 */
import {
  type LotAction,
  caseStatusLabel,
  codeName,
  formatAmount,
  formatQuantity,
  lotActionEnabled,
  lotStatusLabel,
} from "@/lib/format";
import fc from "fast-check";
import { describe, expect, it } from "vitest";

const ALL_ACTIONS: LotAction[] = [
  "complete-manufacturing",
  "cancel-manufacturing-completion",
  "instruct-shipping",
  "complete-shipping",
  "instruct-item-conversion",
  "cancel-item-conversion-instruction",
];

/** lotActionEnabled が true を返しうる status (実装の switch と同値)。 */
const ACTIONABLE_STATUSES = ["manufacturing", "manufactured", "shipping_instructed", "conversion_instructed"];

const stripGrouping = (s: string) => s.replace(/,/g, "");

describe("format.ts PBT (fast-check)", () => {
  it("formatAmount: 任意の安全整数で桁区切りは可逆", () => {
    fc.assert(
      fc.property(fc.integer({ min: Number.MIN_SAFE_INTEGER, max: Number.MAX_SAFE_INTEGER }), (n) => {
        expect(Number(stripGrouping(formatAmount(n)))).toBe(n);
      }),
    );
  });

  it("formatAmount: 非負整数は 3 桁ごとのカンマ区切り形式", () => {
    fc.assert(
      fc.property(fc.integer({ min: 0, max: Number.MAX_SAFE_INTEGER }), (n) => {
        expect(formatAmount(n)).toMatch(/^\d{1,3}(,\d{3})*$/);
      }),
    );
  });

  it("formatQuantity: 小数部は最大 3 桁", () => {
    fc.assert(
      fc.property(
        fc.double({ min: -1e9, max: 1e9, noNaN: true, noDefaultInfinity: true }),
        (x) => {
          const frac = stripGrouping(formatQuantity(x)).split(".")[1] ?? "";
          expect(frac.length).toBeLessThanOrEqual(3);
        },
      ),
    );
  });

  it("formatQuantity: 丸め誤差は 0.0005 以内で可逆", () => {
    fc.assert(
      fc.property(
        fc.double({ min: -1e9, max: 1e9, noNaN: true, noDefaultInfinity: true }),
        (x) => {
          const parsed = Number(stripGrouping(formatQuantity(x)));
          expect(Math.abs(parsed - x)).toBeLessThanOrEqual(0.0005);
        },
      ),
    );
  });

  it("lotActionEnabled: 既知の遷移可能 status 以外では全アクションが無効", () => {
    const unknownStatus = fc
      .string()
      .filter((s) => !ACTIONABLE_STATUSES.includes(s));
    fc.assert(
      fc.property(unknownStatus, (status) => {
        for (const action of ALL_ACTIONS) {
          expect(lotActionEnabled(action, status)).toBe(false);
        }
      }),
    );
    for (const action of ALL_ACTIONS) {
      expect(lotActionEnabled(action, null)).toBe(false);
      expect(lotActionEnabled(action, undefined)).toBe(false);
    }
  });

  it("lotActionEnabled: どの status でも有効アクションは互いに排他しない範囲で決定的", () => {
    // 同じ (action, status) は常に同じ結果 (参照透過)。ランダム status で 2 回評価して一致を確認
    fc.assert(
      fc.property(
        fc.constantFrom(...ALL_ACTIONS),
        fc.oneof(fc.string(), fc.constantFrom(...ACTIONABLE_STATUSES)),
        (action, status) => {
          expect(lotActionEnabled(action, status)).toBe(lotActionEnabled(action, status));
        },
      ),
    );
  });

  it("lotStatusLabel / caseStatusLabel: 未知 status は入力をそのまま返す", () => {
    const unknown = fc.string({ minLength: 1 }).filter(
      (s) =>
        ![
          "manufacturing",
          "manufactured",
          "shipping_instructed",
          "shipped",
          "conversion_instructed",
          "registered",
          "appraised",
          "contracted",
          "shipping_completed",
          "estimating",
          "estimate_submitted",
          "determined",
          "delivered",
          "designated",
          "resulted",
        ].includes(s),
    );
    fc.assert(
      fc.property(unknown, (s) => {
        expect(lotStatusLabel(s)).toBe(s);
        expect(caseStatusLabel(null, s)).toBe(s);
      }),
    );
  });

  it("codeName: name の有無でフォーマットが一貫し、常に code を含む", () => {
    fc.assert(
      fc.property(
        fc.integer({ min: 0, max: 999999 }),
        fc.option(fc.string({ minLength: 1 }), { nil: null }),
        (code, name) => {
          const out = codeName({ code, name });
          expect(out).toContain(String(code));
          if (name) {
            expect(out).toBe(`${name} (${code})`);
          } else {
            expect(out).toBe(String(code));
          }
        },
      ),
    );
  });
});
