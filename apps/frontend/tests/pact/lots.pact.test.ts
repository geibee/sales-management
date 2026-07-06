/**
 * Pact コンシューマテスト — pacts/frontend-sales-management.json の生成元。
 *
 * 従来この pact ファイルは手書きで、実クライアントと乖離していた
 * (complete-manufacturing の body が {date, version} なのに
 * {manufacturingCompletedDate} になっている等)。本テストは実際の
 * `apiGet` / `apiSend` + 生成 Zod スキーマ (schemas.LotResponse) を
 * pact mock server に向けて実行し、「フロントが本当に送る/検証する形」
 * から pact を生成する。コミット済み pact との差分は git diff で現れる。
 *
 * provider state 名・path は backend の
 * tests/SalesManagement.Tests/Pact/StateHandlers.fs のキーと一致させること。
 */
import { existsSync, unlinkSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { MatchersV3, PactV3 } from "@pact-foundation/pact";
import { beforeAll, describe, expect, it, vi } from "vitest";
import { server } from "../support/server";

const { like, integer, decimal, regex, eachLike } = MatchersV3;

const PACT_DIR = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../../../pacts");
const PACT_FILE = path.join(PACT_DIR, "frontend-sales-management.json");

// 実 HTTP を pact mock server に届かせるため、MSW の interception を止める
// (setup.ts の beforeAll(listen) の後に走る)。
// pact ファイルは毎回作り直す: 残骸との merge 衝突 (同 description で内容差)
// を避け、生成結果を常に「現在のテストが定義する契約」と一致させるため。
beforeAll(() => {
  server.close();
  if (existsSync(PACT_FILE)) unlinkSync(PACT_FILE);
});

const provider = new PactV3({
  consumer: "frontend",
  provider: "sales-management",
  dir: PACT_DIR,
  logLevel: "warn",
});

/** schemas.LotResponse の必須フィールドを満たす応答 (matcher 付き)。 */
function lotResponseBody(status: string, version: number) {
  const codeName = { code: integer(10), name: like("名称") };
  return {
    lotNumber: like("lot-1234"),
    status: regex("manufacturing|manufactured|shipping_instructed|shipped|conversion_instructed", status),
    version: integer(version),
    division: codeName,
    department: codeName,
    section: codeName,
    processCategory: codeName,
    inspectionCategory: codeName,
    manufacturingCategory: codeName,
    details: eachLike({
      itemCategory: regex("general|premium|custom", "general"),
      productCategoryCode: like("PC1"),
      lengthSpecLower: decimal(1.5),
      thicknessSpecLower: decimal(0.5),
      thicknessSpecUpper: decimal(1.2),
      qualityGrade: like("A"),
      count: integer(10),
      quantity: decimal(12.5),
    }),
  };
}

/**
 * 実クライアントを mock server の baseURL で読み込み直す。
 * api-client.ts はモジュールロード時に VITE_API_BASE_URL を確定するため、
 * stubEnv → resetModules → dynamic import の順で再評価させる。
 */
async function loadClient(baseUrl: string) {
  vi.stubEnv("VITE_API_BASE_URL", baseUrl);
  vi.resetModules();
  const apiClient = await import("@/lib/api-client");
  const contracts = await import("@/contracts");
  return { ...apiClient, schemas: contracts.schemas };
}

describe("Pact consumer: lots", () => {
  it("GET /lots/{lotNumber} で manufacturing 状態の lot を取得", async () => {
    provider
      .given("lot-1234 が manufacturing 状態で存在する")
      .uponReceiving("GET /lots/{lotNumber} で manufacturing 状態の lot を取得")
      .withRequest({ method: "GET", path: "/lots/lot-1234" })
      .willRespondWith({
        status: 200,
        headers: { "Content-Type": "application/json" },
        body: lotResponseBody("manufacturing", 1),
      });

    await provider.executeTest(async (mockServer) => {
      const { apiGet, schemas } = await loadClient(mockServer.url);
      const lot = await apiGet("/lots/lot-1234", schemas.LotResponse);
      expect(lot.status).toBe("manufacturing");
      expect(lot.version).toBe(1);
    });
  });

  it("POST /lots/{lotNumber}/complete-manufacturing で manufacturing 完了", async () => {
    provider
      .given("lot-1234 が manufacturing 状態で存在する")
      .uponReceiving("POST /lots/{lotNumber}/complete-manufacturing で manufacturing 完了")
      .withRequest({
        method: "POST",
        path: "/lots/lot-1234/complete-manufacturing",
        headers: { "Content-Type": "application/json" },
        body: { date: regex("\\d{4}-\\d{2}-\\d{2}", "2026-04-27"), version: integer(1) },
      })
      .willRespondWith({
        status: 200,
        headers: { "Content-Type": "application/json" },
        body: lotResponseBody("manufactured", 2),
      });

    await provider.executeTest(async (mockServer) => {
      const { apiSend, schemas } = await loadClient(mockServer.url);
      // use-lot.ts の completeManufacturing と同じ呼び出し形
      const lot = await apiSend(
        "POST",
        "/lots/lot-1234/complete-manufacturing",
        { date: "2026-04-27", version: 1 },
        schemas.LotResponse,
      );
      expect(lot.status).toBe("manufactured");
      expect(lot.version).toBe(2);
    });
  });
});
