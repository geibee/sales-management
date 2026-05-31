/**
 * Test fixtures — small factory helpers that return shapes matching
 * the OpenAPI-generated zod schemas in `src/contracts/`.
 *
 * Each factory accepts a partial override so callers can express
 * "default lot, but status = shipping" in one line:
 *
 *   const lot = makeLot({ status: "shipping" });
 *
 * Keep these factories side-effect free; nothing here may import from
 * `tests/support/server.ts` or `tests/setup.ts`.
 */
import type {
  AvailableLotsResponse,
  CodeMastersResponse,
  LotResponse,
  PriceCheckResponse,
  ProblemJson,
  SalesCaseDetailResponse,
} from "@/contracts";

type DeepPartial<T> = T extends object ? { [K in keyof T]?: DeepPartial<T[K]> } : T;

// ---- lot ----

export function makeLotDetail(overrides: Partial<LotResponse["details"][number]> = {}) {
  return {
    itemCategory: "general" as const,
    premiumCategory: null,
    productCategoryCode: "P001",
    lengthSpecLower: 100,
    thicknessSpecLower: 1.0,
    thicknessSpecUpper: 2.0,
    qualityGrade: "A",
    count: 10,
    quantity: 100,
    inspectionResultCategory: null,
    ...overrides,
  } satisfies LotResponse["details"][number];
}

export function makeLot(overrides: DeepPartial<LotResponse> = {}): LotResponse {
  const base: LotResponse = {
    status: "manufacturing",
    lotNumber: "L-0001",
    version: 1,
    manufacturingCompletedDate: null,
    shippingDeadlineDate: null,
    shippedDate: null,
    destinationItem: null,
    division: { code: 10, name: "営業1部" },
    department: { code: 110, name: "営業1課" },
    section: { code: 1110, name: "第1係" },
    processCategory: { code: 1, name: "工程A" },
    inspectionCategory: { code: 2, name: "検査B" },
    manufacturingCategory: { code: 3, name: "製造C" },
    details: [makeLotDetail()],
  };
  return { ...base, ...overrides } as LotResponse;
}

// ---- sales case ----

export function makeSalesCase(
  overrides: DeepPartial<SalesCaseDetailResponse> = {},
): SalesCaseDetailResponse {
  const base: SalesCaseDetailResponse = {
    salesCaseNumber: "S-0001",
    caseType: "direct",
    status: "before_appraisal",
    lots: ["L-0001"],
    divisionCode: 10,
    salesDate: "2026-05-01",
    version: 1,
    appraisal: null,
    contract: null,
    shippingInstruction: null,
    shippingCompletion: null,
    reservationPrice: null,
    determination: null,
    delivery: null,
    consignor: null,
    result: null,
  };
  return { ...base, ...overrides } as SalesCaseDetailResponse;
}

// ---- problem+json (RFC 9457) ----

export function makeProblem(overrides: Partial<ProblemJson> = {}): ProblemJson {
  return {
    type: "about:blank",
    title: "Bad Request",
    status: 400,
    detail: "invalid",
    ...overrides,
  };
}

// ---- price quote ----

export function makePriceQuote(overrides: Partial<PriceCheckResponse> = {}): PriceCheckResponse {
  return {
    basePrice: 1000,
    adjustmentRate: 1.0,
    source: "wiremock",
    ...overrides,
  };
}

// ---- code masters ----

export function makeCodeMasters(
  overrides: DeepPartial<CodeMastersResponse> = {},
): CodeMastersResponse {
  const base: CodeMastersResponse = {
    divisions: [
      { code: 10, name: "営業1部" },
      { code: 20, name: "営業2部" },
    ],
    departments: [
      { code: 110, name: "営業1課", divisionCode: 10 },
      { code: 210, name: "営業2課", divisionCode: 20 },
    ],
    sections: [
      { code: 1110, name: "第1係", departmentCode: 110 },
      { code: 2110, name: "第1係", departmentCode: 210 },
    ],
    processCategories: [{ code: 1, name: "工程A" }],
    inspectionCategories: [{ code: 2, name: "検査B" }],
    manufacturingCategories: [{ code: 3, name: "製造C" }],
  };
  return { ...base, ...overrides } as CodeMastersResponse;
}

// ---- available lots (for LotSelectDialog) ----

export function makeAvailableLot(
  overrides: Partial<AvailableLotsResponse["items"][number]> = {},
): AvailableLotsResponse["items"][number] {
  return {
    lotNumber: "L-0001",
    status: "manufactured",
    version: 1,
    manufacturingCompletedDate: "2026-04-01",
    ...overrides,
  } as AvailableLotsResponse["items"][number];
}

export function makeAvailableLotsResponse(
  items: AvailableLotsResponse["items"] = [makeAvailableLot()],
): AvailableLotsResponse {
  return { items, total: items.length };
}
