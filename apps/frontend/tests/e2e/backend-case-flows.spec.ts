import { type Page, expect, test } from "@playwright/test";

/**
 * 3 案件種別 (direct / reservation / consignment) の主要フロー E2E (issue #9 §7)。
 * backend-lifecycle.spec.ts と同じく実 F# backend 前提 (E2E_BACKEND=1 / nightly)。
 *
 * 各フローは「製造完了済みロットを 1 つ用意 → 案件作成 → 種別ごとの
 * 状態遷移を最後まで進める」ところまでを検査する:
 *   - direct:      価格査定 登録 → 売買契約 登録 → 出荷指示 → 出荷完了
 *   - reservation: 予約価格 登録 → 予約 確定 → 予約 納品
 *   - consignment: 委託指定 → 委託結果 登録
 */

test.skip(!process.env.E2E_BACKEND, "Set E2E_BACKEND=1 to run against a live backend");

/** 在庫ロットを作成して製造完了まで進め、lotNumber を返す。 */
async function createManufacturedLot(page: Page): Promise<string> {
  await page.goto("/lots/new");
  await page.fill("input[name=year]", "2026");
  await page.fill("input[name=location]", "E2E");
  // seq はミリ秒由来で衝突回避 (backend-lifecycle.spec.ts と同じ方針)
  await page.fill("input[name=seq]", `${Date.now() % 100000}`);
  await page.getByRole("button", { name: "作成" }).click();
  await page.waitForURL(
    (url) => /^\/lots\/[^/]+$/.test(url.pathname) && !url.pathname.endsWith("/new"),
  );
  const lotNumber = decodeURIComponent(new URL(page.url()).pathname.split("/").pop() ?? "");
  expect(lotNumber).not.toBe("");

  await page.fill("input[id^='製造完了-date']", "2026-04-28");
  await page.getByRole("button", { name: "製造完了を登録" }).click();
  await expect(page.getByText("製造完了 を実行しました")).toBeVisible();
  return lotNumber;
}

/** 案件種別カードを選び、ロットを紐付けて販売案件を作成。詳細ページに遷移した状態で戻る。 */
async function createSalesCase(
  page: Page,
  caseTypeLabel: RegExp,
  lotNumber: string,
): Promise<void> {
  await page.goto("/sales-cases/new");
  await page.getByRole("button", { name: caseTypeLabel }).click();
  await page.getByLabel("販売日").fill("2026-05-01");
  await page.getByRole("button", { name: "ロットを選択" }).click();
  await page.getByRole("checkbox", { name: `ロット ${lotNumber} を選択` }).check();
  await page.getByRole("dialog").getByRole("button", { name: "確定" }).click();
  await page.getByRole("button", { name: "作成", exact: true }).click();
  await page.waitForURL((url) =>
    /^\/(sales|reservation|consignment)-cases\/[^/]+$/.test(url.pathname),
  );
}

/** タイトル text を含む action card 内の submit button を押し、成功 toast を待つ。 */
async function runAction(page: Page, cardTitle: string, buttonName: string): Promise<void> {
  const card = page.locator('[data-slot="card"]', { hasText: cardTitle }).first();
  await card.getByRole("button", { name: buttonName }).click();
  await expect(page.getByText(`${cardTitle} を実行しました`)).toBeVisible();
}

test("direct: 査定 → 契約 → 出荷指示 → 出荷完了", async ({ page }) => {
  const lotNumber = await createManufacturedLot(page);
  await createSalesCase(page, /直接販売/, lotNumber);
  await expect(page.getByRole("heading", { name: /販売案件/ })).toBeVisible();

  await runAction(page, "価格査定 登録", "登録");
  await runAction(page, "売買契約 登録", "登録");
  await runAction(page, "出荷指示", "登録");
  await runAction(page, "出荷完了", "登録");
  // 最終状態: 次アクション pill が「完了」になる (nextActionLabel("shipping_completed"))
  await expect(page.getByText("完了", { exact: true })).toBeVisible();
});

test("reservation: 予約価格 → 確定 → 納品", async ({ page }) => {
  const lotNumber = await createManufacturedLot(page);
  await createSalesCase(page, /^予約/, lotNumber);
  await expect(page.getByRole("heading", { name: /予約販売案件/ })).toBeVisible();

  await runAction(page, "予約価格 登録", "登録");
  await runAction(page, "予約 確定", "確定");
  await runAction(page, "予約 納品", "引き渡し");
});

test("consignment: 委託指定 → 結果入力", async ({ page }) => {
  const lotNumber = await createManufacturedLot(page);
  await createSalesCase(page, /^委託/, lotNumber);
  await expect(page.getByRole("heading", { name: /委託販売案件/ })).toBeVisible();

  await runAction(page, "委託指定", "登録");
  await runAction(page, "委託結果 登録", "登録");
});
