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

const frontendServer = {
  command: "pnpm dev",
  url: "http://localhost:5173",
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
    baseURL: "http://localhost:5173",
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
