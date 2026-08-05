import { expect, test } from "@playwright/test";

/**
 * Smoke test that runs WITHOUT a backend. Verifies the app boots, the home
 * page lists all aggregates, and Guard correctly hides operator-only UI when
 * no token is set.
 *
 * For full lifecycle tests against the F# backend, see backend-lifecycle.spec.ts
 * (run via `pnpm test:e2e:backend` — the API is auto-started by playwright.config.ts).
 */

test("home page renders and lists every aggregate", async ({ page }) => {
  await page.goto("/");
  // サイドバーのブランド表示
  await expect(page.getByText("Sales Management").first()).toBeVisible();
  // サイドバーナビ / クイックアクションに各アグリゲートが並ぶ
  await expect(page.getByText("在庫ロット").first()).toBeVisible();
  await expect(page.getByText("販売案件").first()).toBeVisible();
  await expect(page.getByText("外部価格チェック").first()).toBeVisible();
});

test("RoleBadge shows '未認証' when no token is configured", async ({ page }) => {
  await page.goto("/");
  await expect(page.getByText("未認証").first()).toBeVisible();
});

test("Guard hides the lot creation form for unauthenticated users", async ({ page }) => {
  // backend-lifecycle E2E では実 API の認証が無効なため Guard は permissive になる。
  // このテスト固有の認証 ON 応答を固定し、/auth/config の応答タイミングに依存せず
  // 「認証 ON + JWT なし」の拒否動作を検証する。
  await page.route("**/api/auth/config", async (route) => {
    await route.fulfill({
      json: {
        enabled: true,
        authority: "https://idp.example/realms/e2e",
        audience: "sales-management-api",
      },
    });
  });
  await page.goto("/lots/new");
  await expect(page.getByText("作成には operator 以上のロールが必要です")).toBeVisible();
});

test("external price check page is reachable and renders an idle CTA", async ({ page }) => {
  await page.goto("/external/price-check");
  await expect(page.getByRole("heading", { name: "外部価格チェック" })).toBeVisible();
  await expect(page.getByRole("button", { name: "取得" })).toBeVisible();
});
