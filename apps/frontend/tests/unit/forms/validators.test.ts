import { DateOnlySchema } from "@/forms/validators";
import { describe, expect, it } from "vitest";

describe("forms/validators", () => {
  it("DateOnlySchema enforces yyyy-MM-dd", () => {
    expect(DateOnlySchema.parse("2026-04-28")).toBe("2026-04-28");
    expect(() => DateOnlySchema.parse("2026/4/28")).toThrow();
    expect(() => DateOnlySchema.parse("28-04-2026")).toThrow();
  });
});
