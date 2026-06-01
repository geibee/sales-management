import "@testing-library/jest-dom/vitest";
import { useAuth } from "@/stores/auth-store";
import { cleanup } from "@testing-library/react";
import { afterAll, afterEach, beforeAll, vi } from "vitest";
import { resetCapturedRequests, server } from "./support/server";

// ---- MSW lifecycle ----
// `onUnhandledRequest: "error"` で未モック URL を明示的にエラーにする。
// 通り抜け (silent passthrough) を許すとモック漏れに気付けないため。
beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => {
  cleanup();
  server.resetHandlers();
  resetCapturedRequests();
  // auth-store はモジュールレベル zustand 状態のため、テスト間で前ケース
  // のロールが漏れないよう毎回リセットする。
  useAuth.getState().clear();
  vi.restoreAllMocks();
});
afterAll(() => server.close());

// ---- window.matchMedia ----
// jsdom は matchMedia を実装していない。sonner (Toaster) と next-themes が
// mount 時に呼ぶので、常に「マッチしない」を返す stub を入れる。
if (typeof window.matchMedia !== "function") {
  Object.defineProperty(window, "matchMedia", {
    configurable: true,
    value: vi.fn((query: string) => ({
      matches: false,
      media: query,
      onchange: null,
      addListener: vi.fn(),
      removeListener: vi.fn(),
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      dispatchEvent: vi.fn(),
    })),
  });
}

// ---- window.confirm ----
// 既定は「常に承諾」。破壊系フローを進行させたいため。キャンセル分岐を
// 検証したいテストは個別に `vi.spyOn(window, "confirm")` で上書きする。
vi.stubGlobal(
  "confirm",
  vi.fn(() => true),
);

// ---- URL.createObjectURL / revokeObjectURL ----
// jsdom は両方とも未実装。CSV ダウンロード (`apiDownload`) が両方を使う。
// revoke は undefined を返すだけで十分。createObjectURL は固定値を返す
// stub にして、テストから href をアサートできるようにする。
if (typeof URL.createObjectURL !== "function") {
  Object.defineProperty(URL, "createObjectURL", {
    configurable: true,
    value: vi.fn(() => "blob:mock"),
  });
}
if (typeof URL.revokeObjectURL !== "function") {
  Object.defineProperty(URL, "revokeObjectURL", {
    configurable: true,
    value: vi.fn(),
  });
}

// ---- HTMLAnchorElement.click ----
// `apiDownload` は `<a download>` を組み立てて click する。jsdom の click
// は navigation を試みて "Not implemented: navigation" を投げるので、
// no-op の stub に差し替えてダウンロード経路を検証可能にする。
if (typeof HTMLAnchorElement.prototype.click === "function") {
  HTMLAnchorElement.prototype.click = vi.fn();
}
