/**
 * FE-PBT-NAMEMAP-001 — codeName (lib/format): name が truthy なら
 * `${name} (${code})`、falsy (null / undefined / 空文字) なら String(code)。
 */
import { codeName } from "@/lib/format";
import fc from "fast-check";
import { describe, expect, it } from "vitest";
import { pbtOpts, pbtTimeout } from "./pbt-env";

describe("lib/format codeName (PBT)", () => {
  it(
    "FE-PBT-NAMEMAP-001: name の有無で表示形式が切り替わる",
    () => {
      const arb = fc.record({
        code: fc.integer(),
        name: fc.oneof(fc.constant(null), fc.constant(undefined), fc.string()),
      });
      fc.assert(
        fc.property(arb, (cn) => {
          // 実装は truthiness 判定のため空文字 name は「名称なし」に落ちる
          const expected = cn.name ? `${cn.name} (${cn.code})` : String(cn.code);
          expect(codeName(cn)).toBe(expected);
        }),
        pbtOpts,
      );
    },
    pbtTimeout,
  );
});
