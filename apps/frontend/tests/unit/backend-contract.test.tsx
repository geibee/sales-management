/**
 * Frontend integration smoke — verifies four contract points with the backend:
 *   1. mutation URLs (reservation/consignment) point at `/sales-cases/{id}/...`
 *   2. `<Guard>` consults `/auth/config` instead of `VITE_AUTH_BYPASS`
 *   3. `isVersionMismatch` recognises optimistic-lock conflict types
 *   4. README documents the DevTokenMint flow
 */
import { render, screen, waitFor } from "@testing-library/react";
import * as fs from "node:fs";
import * as path from "node:path";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { SWRConfig } from "swr";
import { Guard } from "@/components/auth/Guard";
import {
  cancelDesignation,
  designateConsignment,
  recordConsignmentResult,
} from "@/hooks/use-consignment-case";
import {
  cancelReservationConfirmation,
  createReservationPrice,
  deliverReservation,
  confirmReservation,
} from "@/hooks/use-reservation-case";
import { ApiError, isVersionMismatch } from "@/lib/api-client";
import { useAuth } from "@/stores/auth-store";

function jsonResponse(body: unknown, status = 200): Response {
  // Status codes 204/205/304 disallow bodies per the Fetch spec; jsdom's
  // Response constructor enforces that, so emit a null body for those.
  const noBody = status === 204 || status === 205 || status === 304;
  return new Response(noBody ? null : JSON.stringify(body), {
    status,
    headers: noBody ? undefined : { "content-type": "application/json" },
  });
}

function mockFetchWith(impl: (url: string, init?: RequestInit) => Response): typeof fetch {
  const fn = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = typeof input === "string" ? input : input.toString();
    return impl(url, init);
  }) as unknown as typeof fetch;
  vi.stubGlobal("fetch", fn);
  return fn;
}

describe("Frontend ↔ backend contract", () => {
  beforeEach(() => {
    useAuth.getState().clear();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  // ---------- mutation URL unification ----------
  describe("mutation URLs target /sales-cases/{id}/{reservation.consignment}/...", () => {
    it("createReservationPrice posts to /sales-cases/{id}/reservation/appraisals", async () => {
      const seen: string[] = [];
      mockFetchWith((url) => {
        seen.push(url);
        return jsonResponse(null, 204);
      });
      await createReservationPrice("S-1", { value: 1 });
      expect(seen[0]).toMatch(/\/sales-cases\/S-1\/reservation\/appraisals$/);
      expect(seen[0]).not.toMatch(/\/reservation-cases\//);
    });

    it("confirmReservation posts to /sales-cases/{id}/reservation/determine", async () => {
      const seen: string[] = [];
      mockFetchWith((url) => {
        seen.push(url);
        return jsonResponse(null, 204);
      });
      await confirmReservation("S-1", { date: "2026-04-30" });
      expect(seen[0]).toMatch(/\/sales-cases\/S-1\/reservation\/determine$/);
    });

    it("cancelReservationConfirmation DELETEs /sales-cases/{id}/reservation/determination", async () => {
      const seen: Array<{ url: string; method?: string; body?: string }> = [];
      mockFetchWith((url, init) => {
        seen.push({ url, method: init?.method, body: init?.body as string | undefined });
        return jsonResponse(null, 204);
      });
      await cancelReservationConfirmation("S-1", 3);
      expect(seen[0].url).toMatch(/\/sales-cases\/S-1\/reservation\/determination$/);
      expect(seen[0].method).toBe("DELETE");
      expect(seen[0].body && JSON.parse(seen[0].body)).toEqual({ version: 3 });
    });

    it("deliverReservation posts to /sales-cases/{id}/reservation/delivery", async () => {
      const seen: string[] = [];
      mockFetchWith((url) => {
        seen.push(url);
        return jsonResponse(null, 204);
      });
      await deliverReservation("S-1", { date: "2026-04-30" });
      expect(seen[0]).toMatch(/\/sales-cases\/S-1\/reservation\/delivery$/);
    });

    it("designateConsignment posts to /sales-cases/{id}/consignment/designate", async () => {
      const seen: string[] = [];
      mockFetchWith((url) => {
        seen.push(url);
        return jsonResponse(null, 204);
      });
      await designateConsignment("S-2", { consignor: "Acme" });
      expect(seen[0]).toMatch(/\/sales-cases\/S-2\/consignment\/designate$/);
      expect(seen[0]).not.toMatch(/\/consignment-cases\//);
    });

    it("cancelDesignation DELETEs /sales-cases/{id}/consignment/designation", async () => {
      const seen: Array<{ url: string; method?: string; body?: string }> = [];
      mockFetchWith((url, init) => {
        seen.push({ url, method: init?.method, body: init?.body as string | undefined });
        return jsonResponse(null, 204);
      });
      await cancelDesignation("S-2", 5);
      expect(seen[0].url).toMatch(/\/sales-cases\/S-2\/consignment\/designation$/);
      expect(seen[0].method).toBe("DELETE");
      expect(seen[0].body && JSON.parse(seen[0].body)).toEqual({ version: 5 });
    });

    it("recordConsignmentResult posts to /sales-cases/{id}/consignment/result", async () => {
      const seen: string[] = [];
      mockFetchWith((url) => {
        seen.push(url);
        return jsonResponse(null, 204);
      });
      await recordConsignmentResult("S-2", { date: "2026-04-30" });
      expect(seen[0]).toMatch(/\/sales-cases\/S-2\/consignment\/result$/);
    });
  });

  // ---------- /auth/config drives Guard ----------
  describe("Guard reads /auth/config (no VITE_AUTH_BYPASS)", () => {
    it("renders children when enabled=false (auth OFF on backend)", async () => {
      mockFetchWith((url) => {
        if (url.endsWith("/auth/config")) return jsonResponse({ enabled: false });
        return jsonResponse({}, 404);
      });
      render(
        <SWRConfig value={{ provider: () => new Map() }}>
          <Guard requiredRole="admin">
            <span>secret</span>
          </Guard>
        </SWRConfig>,
      );
      await waitFor(() => expect(screen.getByText("secret")).toBeInTheDocument());
    });

    it("renders fallback when enabled=true and the user lacks the role", async () => {
      mockFetchWith((url) => {
        if (url.endsWith("/auth/config"))
          return jsonResponse({ enabled: true, authority: "https://idp.example", audience: "api" });
        return jsonResponse({}, 404);
      });
      render(
        <SWRConfig value={{ provider: () => new Map() }}>
          <Guard requiredRole="admin" fallback={<span>denied</span>}>
            <span>secret</span>
          </Guard>
        </SWRConfig>,
      );
      await waitFor(() => expect(screen.getByText("denied")).toBeInTheDocument());
      expect(screen.queryByText("secret")).not.toBeInTheDocument();
    });
  });

  it("VITE_AUTH_BYPASS is no longer referenced in src/", () => {
    const root = path.resolve(__dirname, "../../src");
    const hits: string[] = [];
    const walk = (dir: string) => {
      for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
        const p = path.join(dir, entry.name);
        if (entry.isDirectory()) walk(p);
        else if (/\.(ts|tsx|js|jsx)$/.test(entry.name)) {
          if (fs.readFileSync(p, "utf8").includes("VITE_AUTH_BYPASS")) hits.push(p);
        }
      }
    };
    walk(root);
    expect(hits).toEqual([]);
  });

  // ---------- 409 / version-mismatch detection ----------
  describe("isVersionMismatch", () => {
    it("returns true for 409 with type=optimistic-lock-conflict", () => {
      const err = new ApiError(
        409,
        { type: "optimistic-lock-conflict", title: "Conflict", status: 409 },
        "/sales-cases/S-1/reservation/determine",
      );
      expect(isVersionMismatch(err)).toBe(true);
    });

    it("returns true for the canonical https://errors.example.com/version-mismatch URI", () => {
      const err = new ApiError(
        409,
        { type: "https://errors.example.com/version-mismatch", status: 409 },
        "/sales-cases/S-1/reservation/determine",
      );
      expect(isVersionMismatch(err)).toBe(true);
    });

    it("returns false for non-409 errors", () => {
      const err = new ApiError(
        400,
        { type: "validation-error", status: 400 },
        "/sales-cases/S-1/reservation/determine",
      );
      expect(isVersionMismatch(err)).toBe(false);
    });
  });

  // ---------- README mentions DevTokenMint ----------
  it("frontend/README.md documents DevTokenMint", () => {
    const readme = fs.readFileSync(path.resolve(__dirname, "../../README.md"), "utf8");
    expect(readme).toMatch(/DevTokenMint/);
    expect(readme).toMatch(/--role admin/);
  });
});
