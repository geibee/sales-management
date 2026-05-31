import "@testing-library/jest-dom/vitest";
import { cleanup } from "@testing-library/react";
import { afterAll, afterEach, beforeAll, vi } from "vitest";
import { resetCapturedRequests, server } from "./support/server";

// ---- MSW lifecycle ----
// `onUnhandledRequest: "error"` ensures every test explicitly mocks
// the endpoints it touches — silent passthrough would mask oversight.
beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => {
  cleanup();
  server.resetHandlers();
  resetCapturedRequests();
  vi.restoreAllMocks();
});
afterAll(() => server.close());

// ---- window.matchMedia ----
// jsdom doesn't implement matchMedia; sonner (Toaster) and next-themes
// both call it during mount. Return a deterministic "not matched" stub.
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
// Default to "accept" so destructive flows proceed; tests that exercise
// the cancel branch can re-stub with `vi.spyOn(window, "confirm")`.
vi.stubGlobal(
  "confirm",
  vi.fn(() => true),
);

// ---- URL.createObjectURL / revokeObjectURL ----
// jsdom doesn't implement these; CSV download (`apiDownload`) needs both.
// `vi.fn()` returns undefined which is fine for revoke; createObjectURL
// gets a deterministic stub so tests can assert on the href.
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
// `apiDownload` programmatically creates an <a download> and clicks it.
// jsdom navigates on click which throws "Not implemented: navigation".
// Stub it as a no-op so the download flow can be exercised in tests.
if (typeof HTMLAnchorElement.prototype.click === "function") {
  HTMLAnchorElement.prototype.click = vi.fn();
}
