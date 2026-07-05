import { ProblemJsonSchema } from "@/contracts";
import { describe, expect, it } from "vitest";

describe("contracts hand-written", () => {
  it("ProblemJsonSchema requires status", () => {
    expect(() => ProblemJsonSchema.parse({ title: "Bad" })).toThrow();
    expect(ProblemJsonSchema.parse({ status: 400, title: "Bad" }).status).toBe(400);
  });
});
