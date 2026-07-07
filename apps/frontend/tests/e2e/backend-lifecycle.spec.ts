import { expect, test } from "@playwright/test";

/**
 * End-to-end happy-path test against a live F# backend.
 *
 * E2E_BACKEND=1 のとき playwright.config.ts の webServer が Migrator → API を
 * 自動起動するため、手動の `dotnet run` は不要。前提は dotnet SDK と
 * Postgres (localhost:5432) のみ。API は Authentication.Enabled=false
 * (appsettings.json の既定値) で動くため <Guard> は permissive になる。
 *
 * Run via:
 *   pnpm test:e2e:backend        (= E2E_BACKEND=1 playwright test)
 *
 * ロット ID は seq に時刻由来の値を使って衝突を避けるので、DB は空でなくてよい。
 */

test.skip(!process.env.E2E_BACKEND, "Set E2E_BACKEND=1 to run against a live backend");

test.describe
  .serial("lot lifecycle", () => {
    // 作成されたロットの詳細ページ path。CSV テストが同じロットを再利用する
    let lotPath = "";

    test("lot lifecycle: create → complete-manufacturing → instruct-shipping → complete-shipping", async ({
      page,
    }) => {
      await page.goto("/lots/new");

      await page.fill("input[name=year]", "2026");
      await page.fill("input[name=location]", "E2E");
      await page.fill("input[name=seq]", `${Date.now() % 100000}`);
      await page.getByRole("button", { name: "作成" }).click();

      // After create, navigation uses the server-returned lotNumber (string).
      // /lots/new の見出し「在庫ロットを作成」も /在庫ロット/ にマッチするため、
      // 見出しではなく URL が詳細ページへ変わるのを待ってから path を記録する
      await page.waitForURL(
        (url) => /^\/lots\/[^/]+$/.test(url.pathname) && !url.pathname.endsWith("/new"),
      );
      lotPath = new URL(page.url()).pathname;
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
      // 直前のテストで作成したロットの詳細ページから export する
      expect(lotPath).not.toBe("");
      await page.goto(lotPath);
      const downloadPromise = page.waitForEvent("download");
      await page.getByRole("button", { name: "CSV エクスポート" }).click();
      const download = await downloadPromise;
      expect(download.suggestedFilename()).toMatch(/^lots_\d{8}\.csv$/);
    });
  });
