/**
 * `lib/utils.ts` — `cn()` (clsx + tailwind-merge) の単体 oracle (issue #9 §7)。
 */
import { cn } from "@/lib/utils";
import { describe, expect, it } from "vitest";

describe("lib/utils cn", () => {
  it("複数 class を空白結合する", () => {
    expect(cn("a", "b")).toBe("a b");
  });

  it("falsy (false / undefined / null) を落とす", () => {
    expect(cn("a", false && "b", undefined, null, "c")).toBe("a c");
  });

  it("tailwind の競合 class は後勝ちでマージされる", () => {
    expect(cn("p-2", "p-4")).toBe("p-4");
    expect(cn("text-red-500", "text-blue-500")).toBe("text-blue-500");
  });

  it("object / array 形式の条件付き class を解決する", () => {
    expect(cn({ a: true, b: false }, ["c", { d: true }])).toBe("a c d");
  });
});
