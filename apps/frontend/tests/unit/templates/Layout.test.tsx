/**
 * レイアウト 3 点（計画 P2-4 — 刷新で書き換え・0 テスト）。
 *   - `Sidebar`  : ナビ項目/グループ、件数バッジ、ロール別フッター表示
 *   - `Topbar`   : パンくず生成 (crumbsFor)、API 稼働インジケータの OK/DOWN
 *   - `Shell`    : Sidebar + Topbar + children の合成描画
 *
 * いずれも `<Link>` / `useRouterState` を使うので `renderWithRouter`。
 * Sidebar は /lots・/sales-cases を、Topbar は /health を SWR fetch するため
 * MSW で都度モックする。ロールは `Guard.test.tsx` 同様の最小 JWT を注入する。
 */
import { Shell } from "@/components/templates/Shell";
import { Sidebar } from "@/components/templates/Sidebar";
import { Topbar } from "@/components/templates/Topbar";
import { useAuth } from "@/stores/auth-store";
import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { renderWithRouter } from "../../support/render";
import { server } from "../../support/server";

function b64url(obj: object): string {
  return Buffer.from(JSON.stringify(obj)).toString("base64url");
}
function makeToken(roles: string[]): string {
  const header = b64url({ alg: "none", typ: "JWT" });
  const payload = b64url({ sub: "test-user", realm_access: { roles } });
  return `${header}.${payload}.sig`;
}

/** Sidebar の件数取得 (/lots・/sales-cases, limit=1) をモックする。 */
function mockSidebarCounts(lotsTotal = 0, casesTotal = 0) {
  server.use(
    http.get("/api/lots", () =>
      HttpResponse.json({ items: [], total: lotsTotal, limit: 1, offset: 0 }),
    ),
    http.get("/api/sales-cases", () =>
      HttpResponse.json({ items: [], total: casesTotal, limit: 1, offset: 0 }),
    ),
  );
}

function mockHealth(ok: boolean) {
  server.use(
    http.get("/api/health", () =>
      ok ? HttpResponse.json({ status: "ok" }) : HttpResponse.json({}, { status: 503 }),
    ),
  );
}

describe("<Sidebar>", () => {
  it("ナビ項目とグループ見出しが描画される", async () => {
    mockSidebarCounts();
    renderWithRouter(<Sidebar />);

    // グループ見出し
    for (const g of ["メイン", "在庫管理", "販売管理", "外部連携"]) {
      expect(await screen.findByText(g)).toBeInTheDocument();
    }
    // 主要リンクの href
    expect(screen.getByRole("link", { name: /ダッシュボード/ })).toHaveAttribute("href", "/");
    expect(screen.getByRole("link", { name: /在庫ロット/ })).toHaveAttribute("href", "/lots");
    expect(screen.getByRole("link", { name: /販売案件/ })).toHaveAttribute("href", "/sales-cases");
    expect(screen.getByRole("link", { name: /外部価格チェック/ })).toHaveAttribute(
      "href",
      "/external/price-check",
    );
  });

  it("lots / sales-cases の total を件数バッジに出す", async () => {
    mockSidebarCounts(7, 12);
    renderWithRouter(<Sidebar />);
    expect(await screen.findByText("7")).toBeInTheDocument();
    expect(await screen.findByText("12")).toBeInTheDocument();
  });

  it("ロール別フッター: admin トークンは AD イニシャルと admin バッジ", async () => {
    mockSidebarCounts();
    useAuth.getState().setToken(makeToken(["admin"]));
    const { container } = renderWithRouter(<Sidebar />);
    expect(await screen.findByText("AD")).toBeInTheDocument();
    // brand 副題にも "admin" があるため、ロールバッジは footer 内に限定して見る。
    const footer = container.querySelector(".rail-footer") as HTMLElement;
    expect(within(footer).getByText("admin")).toBeInTheDocument();
  });

  it("ロール別フッター: operator は OP、未認証は VW + 未認証バッジ", async () => {
    mockSidebarCounts();
    useAuth.getState().setToken(makeToken(["operator"]));
    const { unmount } = renderWithRouter(<Sidebar />);
    expect(await screen.findByText("OP")).toBeInTheDocument();
    unmount();

    useAuth.getState().clear();
    mockSidebarCounts();
    renderWithRouter(<Sidebar />);
    expect(await screen.findByText("VW")).toBeInTheDocument();
    expect(screen.getByText("未認証")).toBeInTheDocument();
  });
});

describe("<Topbar> (パンくず crumbsFor)", () => {
  it("ルートは ダッシュボード のみ", async () => {
    mockHealth(true);
    renderWithRouter(<Topbar />, { initialPath: "/" });
    const nav = await screen.findByRole("navigation", { name: "パンくず" });
    expect(nav).toHaveTextContent("ダッシュボード");
  });

  it("/lots: 在庫管理 / 在庫ロット", async () => {
    mockHealth(true);
    renderWithRouter(<Topbar />, { initialPath: "/lots" });
    const nav = await screen.findByRole("navigation", { name: "パンくず" });
    expect(nav).toHaveTextContent("在庫管理");
    expect(nav).toHaveTextContent("在庫ロット");
  });

  it("/lots/L-1: 末尾に mono のロット番号、在庫ロットは一覧リンク化", async () => {
    mockHealth(true);
    renderWithRouter(<Topbar />, { initialPath: "/lots/L-1" });
    expect(await screen.findByText("L-1")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "在庫ロット" })).toHaveAttribute("href", "/lots");
  });

  it("/reservation-cases/R-1: 販売管理グループとして扱い 販売案件 へ戻れる", async () => {
    mockHealth(true);
    renderWithRouter(<Topbar />, { initialPath: "/reservation-cases/R-1" });
    expect(await screen.findByText("R-1")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "販売案件" })).toHaveAttribute("href", "/sales-cases");
  });

  it("/external/price-check: 外部連携 / 外部価格チェック", async () => {
    mockHealth(true);
    renderWithRouter(<Topbar />, { initialPath: "/external/price-check" });
    const nav = await screen.findByRole("navigation", { name: "パンくず" });
    expect(nav).toHaveTextContent("外部連携");
    expect(nav).toHaveTextContent("外部価格チェック");
  });

  it("ヘルス OK は OK ラベル", async () => {
    mockHealth(true);
    renderWithRouter(<Topbar />, { initialPath: "/" });
    expect(await screen.findByText("OK")).toBeInTheDocument();
  });

  it("ヘルス 503 は DOWN ラベル", async () => {
    mockHealth(false);
    renderWithRouter(<Topbar />, { initialPath: "/" });
    expect(await screen.findByText("DOWN")).toBeInTheDocument();
  });
});

describe("<Shell>", () => {
  it("Sidebar・Topbar・children を合成して描画する", async () => {
    mockSidebarCounts();
    mockHealth(true);
    renderWithRouter(
      <Shell>
        <div data-testid="page-content">本文</div>
      </Shell>,
      { initialPath: "/" },
    );
    // children
    expect(await screen.findByTestId("page-content")).toHaveTextContent("本文");
    // Sidebar マーカー
    expect(screen.getByText("Sales Management")).toBeInTheDocument();
    // Topbar パンくず
    await waitFor(() =>
      expect(screen.getByRole("navigation", { name: "パンくず" })).toBeInTheDocument(),
    );
  });
});
