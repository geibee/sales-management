/**
 * Phase 2a — `<Guard>` のロール階層マトリクス (FE-COMP-GUARD-001..008)。
 *
 * oracle は `docs/frontend-component-page-test-plan.md` §Role Matrix の表。
 * 各ケースは以下 3 要素の組み合わせを検査する:
 *   - バックエンド `/auth/config` の `enabled` (true / false)
 *   - `useAuth` の JWT に入っているロール
 *   - `<Guard requiredRole=...>` で要求しているロール
 *
 * `<Guard>` は `useAuthConfig` が解決するまで何も描画しないため、
 * 各ケースは `getBy*` ではなく `findBy*` を使う必要がある (SWR の
 * 初回ロード窓を吸収するため)。
 */
import { Guard } from "@/components/organisms/auth/Guard";
import { useAuth } from "@/stores/auth-store";
import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it } from "vitest";
import { renderWithApp } from "../../../support/render";
import { server } from "../../../support/server";

const CHILDREN = "secret";
const FALLBACK = "denied";

const child = <span>{CHILDREN}</span>;
const fallback = <span>{FALLBACK}</span>;

function b64url(obj: object): string {
  return Buffer.from(JSON.stringify(obj)).toString("base64url");
}

/**
 * `jose.decodeJwt` が受け取れる最小限の JWT を生成する。decodeJwt は
 * payload しか読まないので、署名は固定の "sig" でよい。
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

describe("<Guard> ロール階層マトリクス (FE-COMP-GUARD-001..008)", () => {
  beforeEach(() => {
    useAuth.getState().clear();
  });

  it("FE-COMP-GUARD-001: 認証 OFF + 未ログイン + admin 要求 → children 表示", async () => {
    mockAuthConfig(false);
    renderWithApp(
      <Guard requiredRole="admin" fallback={fallback}>
        {child}
      </Guard>,
    );
    expect(await screen.findByText(CHILDREN)).toBeInTheDocument();
    expect(screen.queryByText(FALLBACK)).not.toBeInTheDocument();
  });

  it("FE-COMP-GUARD-002: 認証 ON + 未ログイン + viewer 要求 → fallback 表示", async () => {
    mockAuthConfig(true);
    renderWithApp(
      <Guard requiredRole="viewer" fallback={fallback}>
        {child}
      </Guard>,
    );
    expect(await screen.findByText(FALLBACK)).toBeInTheDocument();
    expect(screen.queryByText(CHILDREN)).not.toBeInTheDocument();
  });

  it("FE-COMP-GUARD-003: 認証 ON + viewer トークン + viewer 要求 → children 表示", async () => {
    mockAuthConfig(true);
    useAuth.getState().setToken(makeToken(["viewer"]));
    renderWithApp(
      <Guard requiredRole="viewer" fallback={fallback}>
        {child}
      </Guard>,
    );
    expect(await screen.findByText(CHILDREN)).toBeInTheDocument();
  });

  it("FE-COMP-GUARD-004: 認証 ON + viewer トークン + operator 要求 → fallback 表示", async () => {
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

  it("FE-COMP-GUARD-005: 認証 ON + operator トークン + viewer 要求 → children 表示 (上位ロールは下位を満たす)", async () => {
    mockAuthConfig(true);
    useAuth.getState().setToken(makeToken(["operator"]));
    renderWithApp(
      <Guard requiredRole="viewer" fallback={fallback}>
        {child}
      </Guard>,
    );
    expect(await screen.findByText(CHILDREN)).toBeInTheDocument();
  });

  it("FE-COMP-GUARD-006: 認証 ON + operator + operator 要求 → children 表示", async () => {
    mockAuthConfig(true);
    useAuth.getState().setToken(makeToken(["operator"]));
    renderWithApp(
      <Guard requiredRole="operator" fallback={fallback}>
        {child}
      </Guard>,
    );
    expect(await screen.findByText(CHILDREN)).toBeInTheDocument();
  });

  it("FE-COMP-GUARD-007: 認証 ON + operator + admin 要求 → fallback 表示", async () => {
    mockAuthConfig(true);
    useAuth.getState().setToken(makeToken(["operator"]));
    renderWithApp(
      <Guard requiredRole="admin" fallback={fallback}>
        {child}
      </Guard>,
    );
    expect(await screen.findByText(FALLBACK)).toBeInTheDocument();
  });

  it("FE-COMP-GUARD-008: 認証 ON + admin → viewer / operator / admin の全要求で children 表示", async () => {
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
