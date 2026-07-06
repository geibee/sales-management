import { existsSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { expect, test } from "@playwright/test";

/**
 * Visual regression — バックエンド不要の安定ページをスクリーンショット比較する。
 *
 * baseline (visual.spec.ts-snapshots/) は **CI ランナー上で生成したものを正**とする
 * (ローカルとはフォントレンダリングが異なるため)。生成・更新は
 * `.ci/visual-baseline-request` の内容を変更して push → visual-baseline.yml が
 * `--update-snapshots` で再生成し bot がコミットする。
 *
 * baseline が未生成の環境では skip する (VISUAL_UPDATE=1 のときは生成モードなので走る)。
 */

const SNAPSHOT_DIR = path.join(path.dirname(fileURLToPath(import.meta.url)), "visual.spec.ts-snapshots");
const UPDATING = process.env.VISUAL_UPDATE === "1";

function requireBaseline(name: string) {
  const file = path.join(SNAPSHOT_DIR, `${name}-chromium-linux.png`);
  test.skip(
    !UPDATING && !existsSync(file),
    `baseline 未生成 (${name})。.ci/visual-baseline-request を更新して push すると CI が生成する`,
  );
}

const SCREENSHOT_OPTIONS = {
  fullPage: true,
  // ローディングスピナー等の CSS アニメーションを止めて安定化
  animations: "disabled",
  // アンチエイリアス差などの微小ノイズは許容する
  maxDiffPixelRatio: 0.02,
} as const;

test.describe("visual regression (バックエンド不要ページ)", () => {
  test("home ページ", async ({ page }) => {
    requireBaseline("home");
    await page.goto("/");
    await expect(page.getByText("Sales Management").first()).toBeVisible();
    await expect(page).toHaveScreenshot("home.png", SCREENSHOT_OPTIONS);
  });

  test("Guard 表示 (未認証で /lots/new)", async ({ page }) => {
    requireBaseline("lots-new-guard");
    await page.goto("/lots/new");
    await expect(page.getByText("作成には operator 以上のロールが必要です")).toBeVisible();
    await expect(page).toHaveScreenshot("lots-new-guard.png", SCREENSHOT_OPTIONS);
  });
});
