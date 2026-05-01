import { makeApi, Zodios, type ZodiosOptions } from "@zodios/core";
import { z } from "zod";

const AuthConfigResponse = z
  .object({
    enabled: z.boolean(),
    authority: z.string().nullish(),
    audience: z.string().nullish(),
  })
  .passthrough();
const LotStatus = z.enum([
  "manufacturing",
  "manufactured",
  "shipping_instructed",
  "shipped",
  "conversion_instructed",
]);
const LotSummary = z
  .object({
    status: LotStatus,
    lotNumber: z.string(),
    version: z.number().int(),
    manufacturingCompletedDate: z.string().nullish(),
  })
  .passthrough();
const LotsListResponse = z
  .object({
    items: z.array(LotSummary),
    total: z.number().int(),
    limit: z.number().int(),
    offset: z.number().int(),
  })
  .passthrough();
const createLot_Body = z
  .object({
    lotNumber: z
      .object({
        year: z.number().int(),
        location: z.string(),
        seq: z.number().int(),
      })
      .partial()
      .passthrough(),
    divisionCode: z.number().int(),
    departmentCode: z.number().int(),
    sectionCode: z.number().int(),
    processCategory: z.number().int(),
    inspectionCategory: z.number().int(),
    manufacturingCategory: z.number().int(),
    details: z.array(
      z
        .object({
          itemCategory: z.string(),
          premiumCategory: z.string(),
          productCategoryCode: z.string(),
          lengthSpecLower: z.number(),
          thicknessSpecLower: z.number(),
          thicknessSpecUpper: z.number(),
          qualityGrade: z.string(),
          count: z.number().int(),
          quantity: z.number(),
          inspectionResultCategory: z.string(),
        })
        .partial()
        .passthrough()
    ),
  })
  .partial()
  .passthrough();
const CreateLotResponse = z
  .object({
    status: LotStatus,
    lotNumber: z.string(),
    version: z.number().int(),
  })
  .passthrough();
const LotResponse = z
  .object({
    status: LotStatus,
    lotNumber: z.string(),
    version: z.number().int(),
    manufacturingCompletedDate: z.string().nullish(),
    shippingDeadlineDate: z.string().nullish(),
    shippedDate: z.string().nullish(),
    destinationItem: z.string().nullish(),
  })
  .passthrough();
const completeManufacturing_Body = z
  .object({ date: z.string(), version: z.number().int().gte(1) })
  .passthrough();
const instructLotShipping_Body = z
  .object({ deadline: z.string(), version: z.number().int().gte(1) })
  .passthrough();
const instructItemConversion_Body = z
  .object({ destinationItem: z.string(), version: z.number().int().gte(1) })
  .passthrough();
const SalesCaseType = z.enum(["direct", "reservation", "consignment"]);
const SalesCaseSummary = z
  .object({
    salesCaseNumber: z.string(),
    caseType: SalesCaseType,
    status: z.string(),
    salesDate: z.string().nullish(),
    divisionCode: z.number().int().nullish(),
  })
  .passthrough();
const SalesCasesListResponse = z
  .object({
    items: z.array(SalesCaseSummary),
    total: z.number().int(),
    limit: z.number().int(),
    offset: z.number().int(),
  })
  .passthrough();
const createSalesCase_Body = z
  .object({
    lots: z.array(z.string()).min(1),
    divisionCode: z.number().int(),
    salesDate: z.string(),
    caseType: z.enum(["direct", "reservation", "consignment"]),
  })
  .passthrough();
const CreatedSalesCaseResponse = z
  .object({
    salesCaseNumber: z.string(),
    status: z.string(),
    version: z.number().int(),
  })
  .passthrough();
const SalesCaseDetailResponse = z
  .object({
    salesCaseNumber: z.string(),
    caseType: SalesCaseType,
    status: z.string(),
    lots: z.array(z.string()),
    divisionCode: z.number().int(),
    salesDate: z.string(),
    version: z.number().int(),
    appraisal: z.object({}).partial().passthrough().nullish(),
    contract: z.object({}).partial().passthrough().nullish(),
    shippingInstruction: z.object({}).partial().passthrough().nullish(),
    shippingCompletion: z.object({}).partial().passthrough().nullish(),
    reservationPrice: z.object({}).partial().passthrough().nullish(),
    determination: z.object({}).partial().passthrough().nullish(),
    delivery: z.object({}).partial().passthrough().nullish(),
    consignor: z.object({}).partial().passthrough().nullish(),
    result: z.object({}).partial().passthrough().nullish(),
  })
  .passthrough();
const createSalesAppraisal_Body = z
  .object({
    type: z.enum(["normal", "customer_contract"]),
    appraisalDate: z.string(),
    deliveryDate: z.string(),
    salesMarket: z.string(),
    baseUnitPriceDate: z.string(),
    periodAdjustmentRateDate: z.string(),
    counterpartyAdjustmentRateDate: z.string(),
    taxExcludedEstimatedTotal: z.number().int(),
    customerContractNumber: z.string().optional(),
    contractAdjustmentRate: z.number().nullish(),
    lotAppraisals: z
      .array(
        z
          .object({
            lotNumber: z.string(),
            detailAppraisals: z
              .array(
                z
                  .object({
                    detailIndex: z.number().int(),
                    baseUnitPrice: z.number().int(),
                    periodAdjustmentRate: z.number(),
                    counterpartyAdjustmentRate: z.number(),
                    exceptionalPeriodAdjustmentRate: z.number().nullish(),
                  })
                  .passthrough()
              )
              .min(1),
          })
          .passthrough()
      )
      .min(1),
    version: z.number().int().gte(1),
  })
  .passthrough();
const createSalesContract_Body = z
  .object({
    contractDate: z.string(),
    person: z.string(),
    buyer: z
      .object({ customerNumber: z.string(), agentName: z.string().nullish() })
      .passthrough(),
    salesType: z.number().int(),
    item: z.string(),
    deliveryMethod: z.string(),
    paymentDeferralCondition: z.string().nullish(),
    salesMethod: z.number().int(),
    usage: z.string().nullish(),
    paymentDeferralAmount: z.number().int().nullish(),
    taxExcludedContractAmount: z.number().int(),
    consumptionTax: z.number().int(),
    taxExcludedPaymentAmount: z.number().int(),
    paymentConsumptionTax: z.number().int(),
    version: z.number().int().gte(1),
  })
  .passthrough();
const createReservationPrice_Body = z
  .object({
    appraisalDate: z.string(),
    reservedLotInfo: z.string(),
    reservedAmount: z.number().int(),
    version: z.number().int().gte(1),
  })
  .passthrough();
const ReservationStatusResponse = z
  .object({ status: z.string(), version: z.number().int() })
  .passthrough();
const confirmReservation_Body = z
  .object({
    determinedDate: z.string(),
    determinedAmount: z.number().int(),
    version: z.number().int().gte(1),
  })
  .passthrough();
const deliverReservation_Body = z
  .object({ deliveryDate: z.string(), version: z.number().int().gte(1) })
  .passthrough();
const designateConsignment_Body = z
  .object({
    consignorName: z.string(),
    consignorCode: z.string(),
    designatedDate: z.string(),
    version: z.number().int().gte(1),
  })
  .passthrough();
const ConsignmentStatusResponse = z
  .object({ status: z.string(), version: z.number().int() })
  .passthrough();
const registerConsignmentResult_Body = z
  .object({
    resultDate: z.string(),
    resultAmount: z.number().int(),
    version: z.number().int().gte(1),
  })
  .passthrough();
const PriceCheckResponse = z
  .object({
    basePrice: z.number(),
    adjustmentRate: z.number().nullish(),
    source: z.string(),
  })
  .passthrough();

export const schemas = {
  AuthConfigResponse,
  LotStatus,
  LotSummary,
  LotsListResponse,
  createLot_Body,
  CreateLotResponse,
  LotResponse,
  completeManufacturing_Body,
  instructLotShipping_Body,
  instructItemConversion_Body,
  SalesCaseType,
  SalesCaseSummary,
  SalesCasesListResponse,
  createSalesCase_Body,
  CreatedSalesCaseResponse,
  SalesCaseDetailResponse,
  createSalesAppraisal_Body,
  createSalesContract_Body,
  createReservationPrice_Body,
  ReservationStatusResponse,
  confirmReservation_Body,
  deliverReservation_Body,
  designateConsignment_Body,
  ConsignmentStatusResponse,
  registerConsignmentResult_Body,
  PriceCheckResponse,
};

const endpoints = makeApi([
  {
    method: "get",
    path: "/api/external/price-check",
    alias: "externalPriceCheck",
    description: `WireMock スタブ等の外部価格 API から参考価格を取得。
リトライ・サーキットブレーカー（Polly）が前段にあり、上流障害時は 502/503 を返す。
&#x60;viewer&#x60; ロール以上で利用可能。
`,
    requestFormat: "json",
    parameters: [
      {
        name: "lotId",
        type: "Query",
        schema: z.string(),
      },
    ],
    response: PriceCheckResponse,
    errors: [
      {
        status: 400,
        description: `lotId 未指定など。Problem Details 形式`,
        schema: z.void(),
      },
      {
        status: 502,
        description: `上流 API がタイムアウト / 異常応答 / パース失敗`,
        schema: z.void(),
      },
      {
        status: 503,
        description: `サーキットブレーカーが OPEN`,
        schema: z.void(),
      },
    ],
  },
  {
    method: "get",
    path: "/auth/config",
    alias: "getAuthConfig",
    description: `フロントエンドが起動時に叩いて認証 ON/OFF と IdP authority を判定する公開エンドポイント。
認証 OFF の場合は &#x60;{ enabled: false }&#x60; のみ返す。
認証 ON の場合は &#x60;{ enabled, authority, audience }&#x60; を返す。
`,
    requestFormat: "json",
    response: AuthConfigResponse,
  },
  {
    method: "get",
    path: "/health",
    alias: "healthCheck",
    requestFormat: "json",
    response: z.void(),
  },
  {
    method: "get",
    path: "/lots",
    alias: "listLots",
    description: `在庫ロットをページング付きで取得する。&#x60;status&#x60; クエリで状態フィルタ、
&#x60;limit&#x60; (1..200, default 50) と &#x60;offset&#x60; (&gt;&#x3D; 0, default 0) でページング。
`,
    requestFormat: "json",
    parameters: [
      {
        name: "status",
        type: "Query",
        schema: z
          .enum([
            "manufacturing",
            "manufactured",
            "shipping_instructed",
            "shipped",
            "conversion_instructed",
          ])
          .optional(),
      },
      {
        name: "limit",
        type: "Query",
        schema: z.number().int().gte(1).lte(200).optional().default(50),
      },
      {
        name: "offset",
        type: "Query",
        schema: z.number().int().gte(0).optional().default(0),
      },
    ],
    response: LotsListResponse,
    errors: [
      {
        status: 400,
        description: `不正リクエスト。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
    ],
  },
  {
    method: "post",
    path: "/lots",
    alias: "createLot",
    requestFormat: "json",
    parameters: [
      {
        name: "body",
        type: "Body",
        schema: createLot_Body,
      },
    ],
    response: CreateLotResponse,
    errors: [
      {
        status: 400,
        description: `不正リクエスト。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
      {
        status: 409,
        description: `楽観的ロック競合（リクエストの version が現在値と一致しない）。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
    ],
  },
  {
    method: "get",
    path: "/lots/:id",
    alias: "getLot",
    requestFormat: "json",
    parameters: [
      {
        name: "id",
        type: "Path",
        schema: z.string(),
      },
    ],
    response: LotResponse,
    errors: [
      {
        status: 400,
        description: `不正リクエスト。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
      {
        status: 404,
        description: `リソースなし。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
    ],
  },
  {
    method: "post",
    path: "/lots/:id/cancel-manufacturing-completion",
    alias: "cancelManufacturingCompletion",
    requestFormat: "json",
    parameters: [
      {
        name: "body",
        type: "Body",
        schema: z.object({ version: z.number().int().gte(1) }).passthrough(),
      },
      {
        name: "id",
        type: "Path",
        schema: z.string(),
      },
    ],
    response: LotResponse,
    errors: [
      {
        status: 400,
        description: `不正リクエスト。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
      {
        status: 404,
        description: `リソースなし。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
      {
        status: 409,
        description: `楽観的ロック競合（リクエストの version が現在値と一致しない）。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
    ],
  },
  {
    method: "post",
    path: "/lots/:id/complete-manufacturing",
    alias: "completeManufacturing",
    description: `ロットを「製造完了」状態に遷移。&#x60;version&#x60; は楽観的ロック用の現在値。
DB 側は &#x60;UPDATE ... WHERE version &#x3D; @expected RETURNING version&#x60; で実装され、
affected rows &#x3D; 0 の場合は 409 Conflict を返す。
`,
    requestFormat: "json",
    parameters: [
      {
        name: "body",
        type: "Body",
        schema: completeManufacturing_Body,
      },
      {
        name: "id",
        type: "Path",
        schema: z.string(),
      },
    ],
    response: LotResponse,
    errors: [
      {
        status: 400,
        description: `不正リクエスト。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
      {
        status: 404,
        description: `リソースなし。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
      {
        status: 409,
        description: `楽観的ロック競合（リクエストの version が現在値と一致しない）。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
    ],
  },
  {
    method: "post",
    path: "/lots/:id/complete-shipping",
    alias: "completeLotShipping",
    requestFormat: "json",
    parameters: [
      {
        name: "body",
        type: "Body",
        schema: completeManufacturing_Body,
      },
      {
        name: "id",
        type: "Path",
        schema: z.string(),
      },
    ],
    response: LotResponse,
    errors: [
      {
        status: 400,
        description: `不正リクエスト。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
      {
        status: 404,
        description: `リソースなし。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
      {
        status: 409,
        description: `楽観的ロック競合（リクエストの version が現在値と一致しない）。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
    ],
  },
  {
    method: "post",
    path: "/lots/:id/instruct-item-conversion",
    alias: "instructItemConversion",
    requestFormat: "json",
    parameters: [
      {
        name: "body",
        type: "Body",
        schema: instructItemConversion_Body,
      },
      {
        name: "id",
        type: "Path",
        schema: z.string(),
      },
    ],
    response: LotResponse,
    errors: [
      {
        status: 400,
        description: `不正リクエスト。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
      {
        status: 404,
        description: `リソースなし。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
      {
        status: 409,
        description: `楽観的ロック競合（リクエストの version が現在値と一致しない）。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
    ],
  },
  {
    method: "delete",
    path: "/lots/:id/instruct-item-conversion",
    alias: "cancelItemConversionInstruction",
    requestFormat: "json",
    parameters: [
      {
        name: "body",
        type: "Body",
        schema: z.object({ version: z.number().int().gte(1) }).passthrough(),
      },
      {
        name: "id",
        type: "Path",
        schema: z.string(),
      },
    ],
    response: LotResponse,
    errors: [
      {
        status: 400,
        description: `不正リクエスト。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
      {
        status: 404,
        description: `リソースなし。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
      {
        status: 409,
        description: `楽観的ロック競合（リクエストの version が現在値と一致しない）。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
    ],
  },
  {
    method: "post",
    path: "/lots/:id/instruct-shipping",
    alias: "instructLotShipping",
    requestFormat: "json",
    parameters: [
      {
        name: "body",
        type: "Body",
        schema: instructLotShipping_Body,
      },
      {
        name: "id",
        type: "Path",
        schema: z.string(),
      },
    ],
    response: LotResponse,
    errors: [
      {
        status: 400,
        description: `不正リクエスト。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
      {
        status: 404,
        description: `リソースなし。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
      {
        status: 409,
        description: `楽観的ロック競合（リクエストの version が現在値と一致しない）。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
    ],
  },
  {
    method: "get",
    path: "/lots/export",
    alias: "exportLotsCsv",
    description: `在庫ロットを CSV ファイルとしてダウンロード（Windows-31J / CP932 エンコーディング）。
&#x60;viewer&#x60; ロール以上で利用可能。実装は &#x60;LotCsvExport.fs&#x60;。
`,
    requestFormat: "json",
    parameters: [
      {
        name: "format",
        type: "Query",
        schema: z.literal("csv").optional().default("csv"),
      },
      {
        name: "status",
        type: "Query",
        schema: z
          .enum([
            "manufacturing",
            "manufactured",
            "shipping_instructed",
            "shipped",
            "conversion_instructed",
          ])
          .optional(),
      },
    ],
    response: z.void(),
    errors: [
      {
        status: 400,
        description: `不正リクエスト。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
    ],
  },
  {
    method: "get",
    path: "/sales-cases",
    alias: "listSalesCases",
    description: `販売案件をページング付きで取得する。&#x60;status&#x60; / &#x60;caseType&#x60; クエリでフィルタ、
&#x60;limit&#x60; (1..200, default 50) と &#x60;offset&#x60; (&gt;&#x3D; 0, default 0) でページング。
`,
    requestFormat: "json",
    parameters: [
      {
        name: "status",
        type: "Query",
        schema: z.string().optional(),
      },
      {
        name: "caseType",
        type: "Query",
        schema: z.enum(["direct", "reservation", "consignment"]).optional(),
      },
      {
        name: "limit",
        type: "Query",
        schema: z.number().int().gte(1).lte(200).optional().default(50),
      },
      {
        name: "offset",
        type: "Query",
        schema: z.number().int().gte(0).optional().default(0),
      },
    ],
    response: SalesCasesListResponse,
    errors: [
      {
        status: 400,
        description: `不正リクエスト。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
    ],
  },
  {
    method: "post",
    path: "/sales-cases",
    alias: "createSalesCase",
    requestFormat: "json",
    parameters: [
      {
        name: "body",
        type: "Body",
        schema: createSalesCase_Body,
      },
    ],
    response: CreatedSalesCaseResponse,
    errors: [
      {
        status: 400,
        description: `不正リクエスト。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
    ],
  },
  {
    method: "get",
    path: "/sales-cases/:id",
    alias: "getSalesCase",
    description: `販売案件 ID から詳細を取得する。&#x60;caseType&#x60; (direct / reservation / consignment) で
サブタイプ固有のフィールドが切り替わる。&#x60;viewer&#x60; ロール以上で利用可能。
`,
    requestFormat: "json",
    parameters: [
      {
        name: "id",
        type: "Path",
        schema: z.string(),
      },
    ],
    response: z
      .object({
        salesCaseNumber: z.string(),
        caseType: SalesCaseType,
        status: z.string(),
        lots: z.array(z.string()),
        divisionCode: z.number().int(),
        salesDate: z.string(),
        version: z.number().int(),
        appraisal: z.object({}).partial().passthrough().nullish(),
        contract: z.object({}).partial().passthrough().nullish(),
        shippingInstruction: z.object({}).partial().passthrough().nullish(),
        shippingCompletion: z.object({}).partial().passthrough().nullish(),
        reservationPrice: z.object({}).partial().passthrough().nullish(),
        determination: z.object({}).partial().passthrough().nullish(),
        delivery: z.object({}).partial().passthrough().nullish(),
        consignor: z.object({}).partial().passthrough().nullish(),
        result: z.object({}).partial().passthrough().nullish(),
      })
      .passthrough(),
    errors: [
      {
        status: 400,
        description: `不正リクエスト。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
      {
        status: 404,
        description: `リソースなし。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
    ],
  },
  {
    method: "delete",
    path: "/sales-cases/:id",
    alias: "deleteSalesCase",
    requestFormat: "json",
    parameters: [
      {
        name: "id",
        type: "Path",
        schema: z.string(),
      },
    ],
    response: z.void(),
    errors: [
      {
        status: 400,
        description: `不正リクエスト。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
      {
        status: 404,
        description: `リソースなし。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
    ],
  },
  {
    method: "post",
    path: "/sales-cases/:id/appraisals",
    alias: "createSalesAppraisal",
    requestFormat: "json",
    parameters: [
      {
        name: "body",
        type: "Body",
        schema: createSalesAppraisal_Body,
      },
      {
        name: "id",
        type: "Path",
        schema: z.string(),
      },
    ],
    response: CreatedSalesCaseResponse,
    errors: [
      {
        status: 400,
        description: `不正リクエスト。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
      {
        status: 404,
        description: `リソースなし。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
      {
        status: 409,
        description: `楽観的ロック競合（リクエストの version が現在値と一致しない）。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
    ],
  },
  {
    method: "put",
    path: "/sales-cases/:id/appraisals",
    alias: "updateSalesAppraisal",
    requestFormat: "json",
    parameters: [
      {
        name: "body",
        type: "Body",
        schema: createSalesAppraisal_Body,
      },
      {
        name: "id",
        type: "Path",
        schema: z.string(),
      },
    ],
    response: CreatedSalesCaseResponse,
    errors: [
      {
        status: 400,
        description: `不正リクエスト。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
      {
        status: 404,
        description: `リソースなし。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
      {
        status: 409,
        description: `楽観的ロック競合（リクエストの version が現在値と一致しない）。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
    ],
  },
  {
    method: "delete",
    path: "/sales-cases/:id/appraisals",
    alias: "deleteSalesAppraisal",
    requestFormat: "json",
    parameters: [
      {
        name: "body",
        type: "Body",
        schema: z.object({ version: z.number().int().gte(1) }).passthrough(),
      },
      {
        name: "id",
        type: "Path",
        schema: z.string(),
      },
    ],
    response: z.void(),
    errors: [
      {
        status: 400,
        description: `不正リクエスト。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
      {
        status: 404,
        description: `リソースなし。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
      {
        status: 409,
        description: `楽観的ロック競合（リクエストの version が現在値と一致しない）。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
    ],
  },
  {
    method: "post",
    path: "/sales-cases/:id/consignment/designate",
    alias: "designateConsignment",
    requestFormat: "json",
    parameters: [
      {
        name: "body",
        type: "Body",
        schema: designateConsignment_Body,
      },
      {
        name: "id",
        type: "Path",
        schema: z.string(),
      },
    ],
    response: ConsignmentStatusResponse,
    errors: [
      {
        status: 400,
        description: `不正リクエスト。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
      {
        status: 404,
        description: `リソースなし。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
      {
        status: 409,
        description: `楽観的ロック競合（リクエストの version が現在値と一致しない）。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
    ],
  },
  {
    method: "delete",
    path: "/sales-cases/:id/consignment/designation",
    alias: "cancelConsignmentDesignation",
    requestFormat: "json",
    parameters: [
      {
        name: "body",
        type: "Body",
        schema: z.object({ version: z.number().int().gte(1) }).passthrough(),
      },
      {
        name: "id",
        type: "Path",
        schema: z.string(),
      },
    ],
    response: ConsignmentStatusResponse,
    errors: [
      {
        status: 400,
        description: `不正リクエスト。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
      {
        status: 404,
        description: `リソースなし。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
      {
        status: 409,
        description: `楽観的ロック競合（リクエストの version が現在値と一致しない）。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
    ],
  },
  {
    method: "post",
    path: "/sales-cases/:id/consignment/result",
    alias: "registerConsignmentResult",
    requestFormat: "json",
    parameters: [
      {
        name: "body",
        type: "Body",
        schema: registerConsignmentResult_Body,
      },
      {
        name: "id",
        type: "Path",
        schema: z.string(),
      },
    ],
    response: ConsignmentStatusResponse,
    errors: [
      {
        status: 400,
        description: `不正リクエスト。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
      {
        status: 404,
        description: `リソースなし。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
      {
        status: 409,
        description: `楽観的ロック競合（リクエストの version が現在値と一致しない）。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
    ],
  },
  {
    method: "post",
    path: "/sales-cases/:id/contracts",
    alias: "createSalesContract",
    requestFormat: "json",
    parameters: [
      {
        name: "body",
        type: "Body",
        schema: createSalesContract_Body,
      },
      {
        name: "id",
        type: "Path",
        schema: z.string(),
      },
    ],
    response: CreatedSalesCaseResponse,
    errors: [
      {
        status: 400,
        description: `不正リクエスト。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
      {
        status: 404,
        description: `リソースなし。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
      {
        status: 409,
        description: `楽観的ロック競合（リクエストの version が現在値と一致しない）。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
    ],
  },
  {
    method: "delete",
    path: "/sales-cases/:id/contracts",
    alias: "deleteSalesContract",
    requestFormat: "json",
    parameters: [
      {
        name: "body",
        type: "Body",
        schema: z.object({ version: z.number().int().gte(1) }).passthrough(),
      },
      {
        name: "id",
        type: "Path",
        schema: z.string(),
      },
    ],
    response: z.void(),
    errors: [
      {
        status: 400,
        description: `不正リクエスト。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
      {
        status: 404,
        description: `リソースなし。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
      {
        status: 409,
        description: `楽観的ロック競合（リクエストの version が現在値と一致しない）。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
    ],
  },
  {
    method: "post",
    path: "/sales-cases/:id/reservation/appraisals",
    alias: "createReservationPrice",
    requestFormat: "json",
    parameters: [
      {
        name: "body",
        type: "Body",
        schema: createReservationPrice_Body,
      },
      {
        name: "id",
        type: "Path",
        schema: z.string(),
      },
    ],
    response: ReservationStatusResponse,
    errors: [
      {
        status: 400,
        description: `不正リクエスト。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
      {
        status: 404,
        description: `リソースなし。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
      {
        status: 409,
        description: `楽観的ロック競合（リクエストの version が現在値と一致しない）。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
    ],
  },
  {
    method: "post",
    path: "/sales-cases/:id/reservation/delivery",
    alias: "deliverReservation",
    requestFormat: "json",
    parameters: [
      {
        name: "body",
        type: "Body",
        schema: deliverReservation_Body,
      },
      {
        name: "id",
        type: "Path",
        schema: z.string(),
      },
    ],
    response: ReservationStatusResponse,
    errors: [
      {
        status: 400,
        description: `不正リクエスト。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
      {
        status: 404,
        description: `リソースなし。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
      {
        status: 409,
        description: `楽観的ロック競合（リクエストの version が現在値と一致しない）。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
    ],
  },
  {
    method: "delete",
    path: "/sales-cases/:id/reservation/determination",
    alias: "cancelReservationConfirmation",
    requestFormat: "json",
    parameters: [
      {
        name: "body",
        type: "Body",
        schema: z.object({ version: z.number().int().gte(1) }).passthrough(),
      },
      {
        name: "id",
        type: "Path",
        schema: z.string(),
      },
    ],
    response: ReservationStatusResponse,
    errors: [
      {
        status: 400,
        description: `不正リクエスト。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
      {
        status: 404,
        description: `リソースなし。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
      {
        status: 409,
        description: `楽観的ロック競合（リクエストの version が現在値と一致しない）。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
    ],
  },
  {
    method: "post",
    path: "/sales-cases/:id/reservation/determine",
    alias: "confirmReservation",
    requestFormat: "json",
    parameters: [
      {
        name: "body",
        type: "Body",
        schema: confirmReservation_Body,
      },
      {
        name: "id",
        type: "Path",
        schema: z.string(),
      },
    ],
    response: ReservationStatusResponse,
    errors: [
      {
        status: 400,
        description: `不正リクエスト。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
      {
        status: 404,
        description: `リソースなし。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
      {
        status: 409,
        description: `楽観的ロック競合（リクエストの version が現在値と一致しない）。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
    ],
  },
  {
    method: "post",
    path: "/sales-cases/:id/shipping-completion",
    alias: "completeSalesCaseShipping",
    requestFormat: "json",
    parameters: [
      {
        name: "body",
        type: "Body",
        schema: completeManufacturing_Body,
      },
      {
        name: "id",
        type: "Path",
        schema: z.string(),
      },
    ],
    response: CreatedSalesCaseResponse,
    errors: [
      {
        status: 400,
        description: `不正リクエスト。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
      {
        status: 404,
        description: `リソースなし。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
      {
        status: 409,
        description: `楽観的ロック競合（リクエストの version が現在値と一致しない）。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
    ],
  },
  {
    method: "post",
    path: "/sales-cases/:id/shipping-instruction",
    alias: "instructSalesCaseShipping",
    requestFormat: "json",
    parameters: [
      {
        name: "body",
        type: "Body",
        schema: completeManufacturing_Body,
      },
      {
        name: "id",
        type: "Path",
        schema: z.string(),
      },
    ],
    response: CreatedSalesCaseResponse,
    errors: [
      {
        status: 400,
        description: `不正リクエスト。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
      {
        status: 404,
        description: `リソースなし。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
      {
        status: 409,
        description: `楽観的ロック競合（リクエストの version が現在値と一致しない）。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
    ],
  },
  {
    method: "delete",
    path: "/sales-cases/:id/shipping-instruction",
    alias: "cancelSalesCaseShippingInstruction",
    requestFormat: "json",
    parameters: [
      {
        name: "body",
        type: "Body",
        schema: z.object({ version: z.number().int().gte(1) }).passthrough(),
      },
      {
        name: "id",
        type: "Path",
        schema: z.string(),
      },
    ],
    response: z.void(),
    errors: [
      {
        status: 400,
        description: `不正リクエスト。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
      {
        status: 404,
        description: `リソースなし。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
      {
        status: 409,
        description: `楽観的ロック競合（リクエストの version が現在値と一致しない）。RFC 9457 Problem Details 形式`,
        schema: z.void(),
      },
    ],
  },
]);

export const api = new Zodios(endpoints);

export function createApiClient(baseUrl: string, options?: ZodiosOptions) {
  return new Zodios(baseUrl, endpoints, options);
}
