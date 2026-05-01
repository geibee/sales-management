import { beforeEach, describe, expect, it } from "vitest";
import { useAuth } from "@/stores/auth-store";

/**
 * Build an unsigned JWT (header.payload.signature). Signature is irrelevant —
 * the store only calls `decodeJwt`, which does not verify.
 */
function base64url(input: string): string {
  // btoa is available in jsdom
  return btoa(input).replace(/=+$/, "").replace(/\+/g, "-").replace(/\//g, "_");
}

function makeToken(roles: string[]): string {
  const header = base64url(JSON.stringify({ alg: "none", typ: "JWT" }));
  const payload = base64url(
    JSON.stringify({ realm_access: { roles }, sub: "user-1", iat: 0, exp: 9999999999 }),
  );
  return `${header}.${payload}.`;
}

describe("auth-store", () => {
  beforeEach(() => {
    useAuth.getState().clear();
  });

  it("starts with no token", () => {
    expect(useAuth.getState().token).toBeNull();
    expect(useAuth.getState().roles.size).toBe(0);
    expect(useAuth.getState().hasRole("viewer")).toBe(false);
  });

  it("decodes roles from a Keycloak-style realm_access token", () => {
    const token = makeToken(["operator"]);
    useAuth.getState().setToken(token);
    expect(useAuth.getState().roles.has("operator")).toBe(true);
    expect(useAuth.getState().userId).toBe("user-1");
  });

  it("hasRole respects the viewer ⊂ operator ⊂ admin hierarchy", () => {
    useAuth.getState().setToken(makeToken(["viewer"]));
    expect(useAuth.getState().hasRole("viewer")).toBe(true);
    expect(useAuth.getState().hasRole("operator")).toBe(false);
    expect(useAuth.getState().hasRole("admin")).toBe(false);

    useAuth.getState().setToken(makeToken(["operator"]));
    expect(useAuth.getState().hasRole("viewer")).toBe(true);
    expect(useAuth.getState().hasRole("operator")).toBe(true);
    expect(useAuth.getState().hasRole("admin")).toBe(false);

    useAuth.getState().setToken(makeToken(["admin"]));
    expect(useAuth.getState().hasRole("viewer")).toBe(true);
    expect(useAuth.getState().hasRole("operator")).toBe(true);
    expect(useAuth.getState().hasRole("admin")).toBe(true);
  });

  it("ignores malformed tokens without throwing", () => {
    useAuth.getState().setToken("not-a-jwt");
    expect(useAuth.getState().roles.size).toBe(0);
    expect(useAuth.getState().userId).toBeNull();
  });

  it("clear() resets everything", () => {
    useAuth.getState().setToken(makeToken(["admin"]));
    useAuth.getState().clear();
    expect(useAuth.getState().token).toBeNull();
    expect(useAuth.getState().roles.size).toBe(0);
    expect(useAuth.getState().hasRole("viewer")).toBe(false);
  });
});
