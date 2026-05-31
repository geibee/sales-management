/**
 * Phase 2a — `<Guard>` role matrix (FE-COMP-GUARD-001..008).
 *
 * The oracle is the table in `docs/frontend-component-page-test-plan.md`
 * §Role Matrix. Each case exercises the combination of:
 *   - backend `/auth/config` `enabled` flag (true / false)
 *   - the JWT roles in `useAuth`
 *   - the `requiredRole` prop on `<Guard>`
 *
 * `<Guard>` blocks render until `useAuthConfig` resolves, so each case
 * has to `findBy*` rather than `getBy*` — that's the SWR loading
 * window collapsing on first paint.
 */
import { Guard } from "@/components/auth/Guard";
import { useAuth } from "@/stores/auth-store";
import { screen } from "@testing-library/react";
import { HttpResponse, http } from "msw";
import { beforeEach, describe, expect, it } from "vitest";
import { renderWithApp } from "../../support/render";
import { server } from "../../support/server";

const CHILDREN = "secret";
const FALLBACK = "denied";

const child = <span>{CHILDREN}</span>;
const fallback = <span>{FALLBACK}</span>;

function b64url(obj: object): string {
  return Buffer.from(JSON.stringify(obj)).toString("base64url");
}

/**
 * Forge a JWT that `jose.decodeJwt` will accept. Only the payload is
 * read — header alg/sig are never verified, so a literal "sig"
 * suffix is fine.
 */
function makeToken(roles: string[]): string {
  const header = b64url({ alg: "none", typ: "JWT" });
  const payload = b64url({ sub: "test-user", realm_access: { roles } });
  return `${header}.${payload}.sig`;
}

function mockAuthConfig(enabled: boolean): void {
  server.use(
    http.get("/api/auth/config", () =>
      HttpResponse.json({ enabled, authority: "https://idp.example/realms/x", audience: "api" }),
    ),
  );
}

describe("<Guard> role matrix (FE-COMP-GUARD-001..008)", () => {
  beforeEach(() => {
    useAuth.getState().clear();
  });

  it("FE-COMP-GUARD-001: auth disabled + anonymous + requiredRole=admin → children", async () => {
    mockAuthConfig(false);
    renderWithApp(
      <Guard requiredRole="admin" fallback={fallback}>
        {child}
      </Guard>,
    );
    expect(await screen.findByText(CHILDREN)).toBeInTheDocument();
    expect(screen.queryByText(FALLBACK)).not.toBeInTheDocument();
  });

  it("FE-COMP-GUARD-002: auth enabled + anonymous + viewer required → fallback", async () => {
    mockAuthConfig(true);
    renderWithApp(
      <Guard requiredRole="viewer" fallback={fallback}>
        {child}
      </Guard>,
    );
    expect(await screen.findByText(FALLBACK)).toBeInTheDocument();
    expect(screen.queryByText(CHILDREN)).not.toBeInTheDocument();
  });

  it("FE-COMP-GUARD-003: enabled + viewer token + viewer required → children", async () => {
    mockAuthConfig(true);
    useAuth.getState().setToken(makeToken(["viewer"]));
    renderWithApp(
      <Guard requiredRole="viewer" fallback={fallback}>
        {child}
      </Guard>,
    );
    expect(await screen.findByText(CHILDREN)).toBeInTheDocument();
  });

  it("FE-COMP-GUARD-004: enabled + viewer token + operator required → fallback", async () => {
    mockAuthConfig(true);
    useAuth.getState().setToken(makeToken(["viewer"]));
    renderWithApp(
      <Guard requiredRole="operator" fallback={fallback}>
        {child}
      </Guard>,
    );
    expect(await screen.findByText(FALLBACK)).toBeInTheDocument();
    expect(screen.queryByText(CHILDREN)).not.toBeInTheDocument();
  });

  it("FE-COMP-GUARD-005: enabled + operator token + viewer required → children (higher role passes)", async () => {
    mockAuthConfig(true);
    useAuth.getState().setToken(makeToken(["operator"]));
    renderWithApp(
      <Guard requiredRole="viewer" fallback={fallback}>
        {child}
      </Guard>,
    );
    expect(await screen.findByText(CHILDREN)).toBeInTheDocument();
  });

  it("FE-COMP-GUARD-006: enabled + operator + operator required → children", async () => {
    mockAuthConfig(true);
    useAuth.getState().setToken(makeToken(["operator"]));
    renderWithApp(
      <Guard requiredRole="operator" fallback={fallback}>
        {child}
      </Guard>,
    );
    expect(await screen.findByText(CHILDREN)).toBeInTheDocument();
  });

  it("FE-COMP-GUARD-007: enabled + operator + admin required → fallback", async () => {
    mockAuthConfig(true);
    useAuth.getState().setToken(makeToken(["operator"]));
    renderWithApp(
      <Guard requiredRole="admin" fallback={fallback}>
        {child}
      </Guard>,
    );
    expect(await screen.findByText(FALLBACK)).toBeInTheDocument();
  });

  it("FE-COMP-GUARD-008: enabled + admin → children for viewer / operator / admin", async () => {
    mockAuthConfig(true);
    useAuth.getState().setToken(makeToken(["admin"]));
    for (const role of ["viewer", "operator", "admin"] as const) {
      const { unmount } = renderWithApp(
        <Guard requiredRole={role} fallback={fallback}>
          <span>{`${CHILDREN}-${role}`}</span>
        </Guard>,
      );
      expect(await screen.findByText(`${CHILDREN}-${role}`)).toBeInTheDocument();
      unmount();
    }
  });
});
