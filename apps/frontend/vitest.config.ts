import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";
import path from "node:path";

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "src"),
    },
  },
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: ["./tests/setup.ts"],
    include: ["src/**/*.test.{ts,tsx}", "tests/unit/**/*.test.{ts,tsx}"],
    exclude: ["tests/e2e/**", "node_modules/**", "dist/**"],
    // FE-EVID-UNIT-001: CI では JUnit XML を artifact 用に出力する。
    // ローカルは default reporter のみ (出力ノイズを避ける)。
    reporters: process.env.CI ? ["default", "junit"] : ["default"],
    outputFile: { junit: "test-results/vitest-junit.xml" },
    coverage: {
      provider: "v8",
      include: ["src/**"],
      // 生成コード・起動エントリ・型宣言はカバレッジ対象外 (テストで駆動する意味がない)。
      // src/routes は Phase 6 (renderWithRealRouter による FE-NAV-*) で駆動される
      exclude: ["src/contracts/generated.ts", "src/routeTree.gen.ts", "src/main.tsx", "src/**/*.d.ts"],
      reporter: ["text-summary", "json-summary"],
      reportsDirectory: "coverage",
    },
  },
});
