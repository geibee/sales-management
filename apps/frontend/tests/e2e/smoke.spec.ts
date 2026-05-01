import { expect, test } from "@playwright/test";

/**
 * Smoke test that runs WITHOUT a backend. Verifies the app boots, the home
 * page lists all aggregates, and Guard correctly hides operator-only UI when
 * no token is set.
 *
 * For full lifecycle tests against the F# backend, see backend-lifecycle.spec.ts
 * (manual run: requires `dotnet run` in another terminal with auth disabled).
 */

test("home page renders and lists every aggregate", async ({ page }) => {
  await page.goto("/");
  await expect(page.getByRole("link", { name: "Sales Management Admin" })).toBeVisible();
  await expect(page.getByText("在庫ロット")).toBeVisible();
  await expect(page.getByText("販売案件")).toBeVisible();
  await expect(page.getByText("外部価格チェック")).toBeVisible();
});

test("RoleBadge shows '未認証' when no token is configured", async ({ page }) => {
  await page.goto("/");
  await expect(page.getByText("未認証").first()).toBeVisible();
});

test("Guard hides the lot creation form for unauthenticated users", async ({ page }) => {
  await page.goto("/lots/new");
  await expect(page.getByText("作成には operator 以上のロールが必要です")).toBeVisible();
});

test("external price check page is reachable and renders an idle CTA", async ({ page }) => {
  await page.goto("/external/price-check");
  await expect(page.getByRole("heading", { name: "外部価格チェック" })).toBeVisible();
  await expect(page.getByRole("button", { name: "取得" })).toBeVisible();
});
