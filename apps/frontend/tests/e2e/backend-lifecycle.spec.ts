import { expect, test } from "@playwright/test";

/**
 * End-to-end happy-path test against a live F# backend.
 *
 * Requires:
 *   1. F# API running at http://localhost:5000 with Authentication.Enabled=false
 *   2. An empty database (or accept that lot/case IDs may collide)
 *
 * Skipped by default. Run via:
 *   E2E_BACKEND=1 pnpm test:e2e backend-lifecycle
 */

test.skip(!process.env.E2E_BACKEND, "Set E2E_BACKEND=1 to run against a live backend");

test("lot lifecycle: create → complete-manufacturing → instruct-shipping → complete-shipping", async ({
  page,
}) => {
  await page.goto("/lots/new");

  // The backend runs with Authentication.Enabled=false, so /auth/config returns
  // { enabled: false } and <Guard> becomes permissive automatically.
  await page.fill("input[name=year]", "2026");
  await page.fill("input[name=location]", "E2E");
  await page.fill("input[name=seq]", `${Date.now() % 100000}`);
  await page.getByRole("button", { name: "作成" }).click();

  // After create, navigation uses the server-returned lotNumber (string).
  await expect(page.getByRole("heading", { name: /在庫ロット/ })).toBeVisible();

  // The state-machine activates only the next-allowed action button.
  // Status is "manufacturing" → 製造完了 button is enabled.
  await page.fill("input[id^='製造完了-date']", "2026-04-28");
  await page.getByRole("button", { name: "製造完了を登録" }).click();
  await expect(page.getByText("製造完了 を実行しました")).toBeVisible();

  // After completion → "manufactured" → 出荷指示 button enabled (deadline date).
  await page.fill("input[id^='出荷指示-date']", "2026-04-29");
  await page.getByRole("button", { name: "出荷指示を登録" }).click();
  await expect(page.getByText("出荷指示 を実行しました")).toBeVisible();

  // After instruction → "shipping_instructed" → 出荷完了 button enabled.
  await page.fill("input[id^='出荷完了-date']", "2026-04-30");
  await page.getByRole("button", { name: "出荷完了を登録" }).click();
  await expect(page.getByText("出荷完了 を実行しました")).toBeVisible();
});

test("CSV export downloads a file", async ({ page }) => {
  await page.goto("/lots/2026-E2E-1");
  const downloadPromise = page.waitForEvent("download");
  await page.getByRole("button", { name: "CSV エクスポート" }).click();
  const download = await downloadPromise;
  expect(download.suggestedFilename()).toMatch(/^lots_\d{8}\.csv$/);
});
