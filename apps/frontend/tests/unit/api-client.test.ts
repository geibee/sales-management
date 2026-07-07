import { ApiError, apiGet, apiSend, describeApiError } from "@/lib/api-client";
import { useAuth } from "@/stores/auth-store";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { z } from "zod";

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
      vi
        .fn()
        .mockResolvedValue(new Response(JSON.stringify({ id: "x", value: 42 }), { status: 200 })),
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
    const headers = fetchMock.mock.calls[0]![1]!.headers as Record<string, string>;
    expect(headers.authorization).toBe("Bearer dummy-token");
  });

  it("FE-ERR-002: clears auth on 401", async () => {
    useAuth.getState().setToken("expired-token");
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValue(
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
    const init = fetchMock.mock.calls[0]![1] as RequestInit;
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

  it("FE-ERR-005: describeApiError emits friendly text for 409 optimistic-lock conflict", () => {
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

/**
 * Phase 7 — `describeApiError` の全 variant oracle (FE-ERR-001..010)。
 * FE-ERR-002 (401) / FE-ERR-005 (409 optimistic-lock) は上の describe が担う。
 */
describe("describeApiError 全 variant (FE-ERR-*)", () => {
  it.each([
    ["FE-ERR-001", 400, "validation failed: salesDate is required"],
    ["FE-ERR-003", 403, "権限がありません"],
    ["FE-ERR-004", 404, "Lot 2026-A-9 not found"],
    ["FE-ERR-006", 422, "unprocessable content"],
    ["FE-ERR-007", 500, "internal failure"],
    ["FE-ERR-008", 502, "upstream unavailable"],
  ] as const)("%s: %i problem+json → `status: detail` を表示", (_id, status, detail) => {
    const err = new ApiError(status, { type: "about:blank", title: "T", status, detail }, "/x");
    expect(describeApiError(err)).toBe(`${status}: ${detail}`);
  });

  it("FE-ERR-001b: detail 欠落の problem は title を表示", () => {
    const err = new ApiError(400, { type: "about:blank", title: "Bad Request", status: 400 }, "/x");
    expect(describeApiError(err)).toBe("400: Bad Request");
  });

  it("FE-ERR-009: network error (fetch reject) → Error message を fallback 表示", () => {
    expect(describeApiError(new TypeError("Failed to fetch"))).toBe("Failed to fetch");
  });

  it("FE-ERR-009b: 非 Error 値も String() で fallback 表示", () => {
    expect(describeApiError("boom")).toBe("boom");
  });

  it("FE-ERR-010: malformed problem body (JSON でない / 空) → `API {status} {path}` fallback", () => {
    expect(describeApiError(new ApiError(500, null, "/lots"))).toBe("API 500 /lots");
    expect(describeApiError(new ApiError(502, "not-json", "/lots"))).toBe("API 502 /lots");
    expect(describeApiError(new ApiError(400, { unexpected: true }, "/lots"))).toBe(
      "API 400 /lots",
    );
  });
});
