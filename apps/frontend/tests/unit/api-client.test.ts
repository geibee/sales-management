import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { z } from "zod";
import { apiGet, apiSend, ApiError, describeApiError } from "@/lib/api-client";
import { useAuth } from "@/stores/auth-store";

const TestSchema = z.object({ id: z.string(), value: z.number() });

describe("api-client", () => {
  beforeEach(() => {
    useAuth.getState().clear();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("apiGet parses a valid JSON response", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        new Response(JSON.stringify({ id: "x", value: 42 }), { status: 200 }),
      ),
    );
    const result = await apiGet("/test", TestSchema);
    expect(result).toEqual({ id: "x", value: 42 });
  });

  it("attaches Authorization header when token is set", async () => {
    useAuth.getState().setToken("dummy-token");
    const fetchMock = vi
      .fn()
      .mockResolvedValue(new Response(JSON.stringify({ id: "x", value: 1 }), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);
    await apiGet("/test", TestSchema);
    const headers = fetchMock.mock.calls[0][1].headers as Record<string, string>;
    expect(headers.authorization).toBe("Bearer dummy-token");
  });

  it("clears auth on 401", async () => {
    useAuth.getState().setToken("expired-token");
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        new Response(JSON.stringify({ status: 401, detail: "expired" }), { status: 401 }),
      ),
    );
    await expect(apiGet("/test", TestSchema)).rejects.toBeInstanceOf(ApiError);
    expect(useAuth.getState().token).toBeNull();
  });

  it("throws ApiError(409) on Conflict", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        new Response(
          JSON.stringify({
            type: "conflict",
            title: "Conflict",
            status: 409,
            detail: "version mismatch",
          }),
          { status: 409 },
        ),
      ),
    );
    await expect(
      apiSend("POST", "/lots/abc/complete-manufacturing", { date: "2026-04-28", version: 1 }),
    ).rejects.toMatchObject({ status: 409 });
  });

  it("apiSend POSTs JSON body (with version)", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));
    vi.stubGlobal("fetch", fetchMock);
    await apiSend("POST", "/lots/abc/complete-manufacturing", {
      date: "2026-04-28",
      version: 1,
    });
    const init = fetchMock.mock.calls[0][1] as RequestInit;
    expect(init.method).toBe("POST");
    expect(init.body).toBe(JSON.stringify({ date: "2026-04-28", version: 1 }));
    expect((init.headers as Record<string, string>)["content-type"]).toBe("application/json");
  });

  it("describeApiError extracts problem+json detail", () => {
    const err = new ApiError(
      400,
      { type: "invalid", title: "Bad", status: 400, detail: "状態が不正です" },
      "/lots/1/complete-shipping",
    );
    expect(describeApiError(err)).toBe("400: 状態が不正です");
  });

  it("describeApiError emits friendly text for 409 optimistic-lock conflict", () => {
    const err = new ApiError(
      409,
      {
        type: "optimistic-lock-conflict",
        title: "Resource was modified by another user",
        status: 409,
        detail: "Lot 1 has been updated. Please reload and try again.",
      },
      "/lots/1/complete-manufacturing",
    );
    expect(describeApiError(err)).toContain("再表示");
  });

  it("describeApiError surfaces backend detail for non-version 409 (e.g. duplicate-resource)", () => {
    const err = new ApiError(
      409,
      {
        type: "https://errors.example.com/duplicate-resource",
        title: "Duplicate resource",
        status: 409,
        detail: "Lot L-1 already exists",
      },
      "/lots",
    );
    expect(describeApiError(err)).toBe("409: Lot L-1 already exists");
  });
});
