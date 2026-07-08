import { defineConfig, devices } from "@playwright/test";

// E2E_BACKEND=1 のとき F# API も webServer として自動起動し、
// backend-lifecycle.spec.ts を無人実行できるようにする (手動 `dotnet run` 不要)。
// 前提: dotnet SDK と Postgres (localhost:5432, app/app) が使えること。
// Migrator でスキーマを準備してから API を起動する。
const backendServer = {
  command: "dotnet run --project tools/Migrator && dotnet run --project src/SalesManagement",
  cwd: "../api-fsharp",
  url: "http://localhost:5000/health",
  reuseExistingServer: !process.env.CI,
  // コールドビルド (restore + build) を含むため長め。CI では事前 build 推奨
  timeout: 300_000,
};

// E2E は既定の 5173 ではなく専用ポートで vite を起動する。
// 5173 は他プロジェクトの dev サーバーが占有していることがあり、
// reuseExistingServer がその「別物のアプリ」を黙って被検体にしてしまう
// (赤 = 本当に壊れている、を守るための決定性対策)。--strictPort で
// ポートが取れない場合はフォールバックせず起動失敗にする。
const E2E_PORT = 5273;

const frontendServer = {
  command: `pnpm dev --port ${E2E_PORT} --strictPort`,
  url: `http://localhost:${E2E_PORT}`,
  reuseExistingServer: !process.env.CI,
  timeout: 60_000,
};

export default defineConfig({
  testDir: "./tests/e2e",
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: 0,
  workers: 1,
  reporter: [["list"]],
  use: {
    baseURL: `http://localhost:${E2E_PORT}`,
    trace: "on-first-retry",
    // playwright install できない環境 (システム提供の chromium を使う CI サンドボックス等)
    // では PW_CHROMIUM_PATH で実行バイナリを差し替えられる
    ...(process.env.PW_CHROMIUM_PATH
      ? { launchOptions: { executablePath: process.env.PW_CHROMIUM_PATH } }
      : {}),
  },
  projects: [
    {
      name: "chromium",
      use: { ...devices["Desktop Chrome"] },
    },
  ],
  webServer: process.env.E2E_BACKEND ? [backendServer, frontendServer] : [frontendServer],
});
