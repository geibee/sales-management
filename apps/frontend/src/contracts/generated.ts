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
        year: z.number().int().gte(1).lte(2147483647),
        location: z
          .string()
          .min(1)
          .regex(/^[^-\s\u0000]+$/u),
        seq: z.number().int().gte(1).lte(2147483647),
      })
      .passthrough(),
    divisionCode: z.number().int().gte(-2147483648).lte(2147483647),
    departmentCode: z.number().int().gte(-2147483648).lte(2147483647),
    sectionCode: z.number().int().gte(-2147483648).lte(2147483647),
    processCategory: z.number().int().gte(-2147483648).lte(2147483647),
    inspectionCategory: z.number().int().gte(-2147483648).lte(2147483647),
    manufacturingCategory: z.number().int().gte(-2147483648).lte(2147483647),
    details: z
      .array(
        z
          .object({
            itemCategory: z.enum(["general", "premium", "custom"]),
            premiumCategory: z
              .string()
              .regex(/^[^\u0000]*$/u)
              .nullish(),
            productCategoryCode: z.string().regex(/^[^\u0000]*$/u),
            lengthSpecLower: z.number().gte(-9999999999).lte(9999999999),
            thicknessSpecLower: z.number().gte(-9999999999).lte(9999999999),
            thicknessSpecUpper: z.number().gte(-9999999999).lte(9999999999),
            qualityGrade: z.string().regex(/^[^\u0000]*$/u),
            count: z.number().int().gte(1).lte(2147483647),
            quantity: z.number().gte(0.001).lte(9999999999),
            inspectionResultCategory: z
              .string()
              .regex(/^[^\u0000]*$/u)
              .nullish(),
          })
          .passthrough()
      )
      .min(1),
  })
  .passthrough();
const CreateLotResponse = z
  .object({
    status: LotStatus,
    lotNumber: z.string(),
    version: z.number().int(),
  })
  .passthrough();
const AvailableLotsResponse = z
  .object({ items: z.array(LotSummary), total: z.number().int() })
  .passthrough();
const CodeMasterItem = z
  .object({ code: z.number().int(), name: z.string() })
  .passthrough();
const DepartmentItem = z
  .object({
    code: z.number().int(),
    name: z.string(),
    divisionCode: z.number().int(),
  })
  .passthrough();
const SectionItem = z
  .object({
    code: z.number().int(),
    name: z.string(),
    departmentCode: z.number().int(),
  })
  .passthrough();
const CodeMastersResponse = z
  .object({
    divisions: z.array(CodeMasterItem),
    departments: z.array(DepartmentItem),
    sections: z.array(SectionItem),
    processCategories: z.array(CodeMasterItem),
    inspectionCategories: z.array(CodeMasterItem),
    manufacturingCategories: z.array(CodeMasterItem),
  })
  .passthrough();
const CodeName = z
  .object({ code: z.number().int(), name: z.string().nullish() })
  .passthrough();
const LotDetailResponse = z
  .object({
    itemCategory: z.enum(["general", "premium", "custom"]),
    premiumCategory: z.string().nullish(),
    productCategoryCode: z.string(),
    lengthSpecLower: z.number(),
    thicknessSpecLower: z.number(),
    thicknessSpecUpper: z.number(),
    qualityGrade: z.string(),
    count: z.number().int(),
    quantity: z.number(),
    inspectionResultCategory: z.string().nullish(),
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
    division: CodeName,
    department: CodeName,
    section: CodeName,
    processCategory: CodeName,
    inspectionCategory: CodeName,
    manufacturingCategory: CodeName,
    details: z.array(LotDetailResponse),
  })
  .passthrough();
const completeManufacturing_Body = z
  .object({
    date: z.string(),
    version: z.number().int().gte(1).lte(2147483647),
  })
  .passthrough();
const instructLotShipping_Body = z
  .object({
    deadline: z.string(),
    version: z.number().int().gte(1).lte(2147483647),
  })
  .passthrough();
const instructItemConversion_Body = z
  .object({
    destinationItem: z.string(),
    version: z.number().int().gte(1).lte(2147483647),
  })
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
    lots: z
      .array(z.string().regex(/^[0-9]{1,9}-[^-\u0000]+-[0-9]{1,9}$/u))
      .min(1),
    divisionCode: z.number().int().gte(-2147483648).lte(2147483647),
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
const EditCaseLotsRequest = z
  .object({
    lots: z
      .array(z.string().regex(/^[0-9]{1,9}-[^-\u0000]+-[0-9]{1,9}$/u))
      .min(1),
    version: z.number().int().gte(1).lte(2147483647),
  })
  .passthrough();
const DirectAppraisal = z
  .object({
    type: z.string(),
    appraisalDate: z.string(),
    deliveryDate: z.string(),
    salesMarket: z.string(),
    taxExcludedEstimatedTotal: z.number().int(),
  })
  .passthrough();
const DirectContract = z
  .object({
    contractDate: z.string(),
    person: z.string(),
    customerNumber: z.string(),
    taxExcludedContractAmount: z.number().int(),
    consumptionTax: z.number().int(),
  })
  .passthrough();
const ShippingInstruction = z
  .object({ instructionDate: z.string() })
  .passthrough();
const ShippingCompletion = z
  .object({ completionDate: z.string() })
  .passthrough();
const DirectSalesCaseDetail = z
  .object({
    salesCaseNumber: z.string(),
    caseType: z.literal("direct"),
    status: z.string(),
    lots: z.array(z.string()),
    divisionCode: z.number().int(),
    salesDate: z.string(),
    version: z.number().int(),
    appraisal: DirectAppraisal.nullable(),
    contract: DirectContract.nullable(),
    shippingInstruction: ShippingInstruction.nullable(),
    shippingCompletion: ShippingCompletion.nullable(),
  })
  .passthrough();
const ReservationPrice = z
  .object({
    appraisalDate: z.string(),
    reservedLotInfo: z.string(),
    reservedAmount: z.number().int(),
  })
  .passthrough();
const ReservationDetermination = z
  .object({ determinedDate: z.string(), determinedAmount: z.number().int() })
  .passthrough();
const ReservationDelivery = z
  .object({ deliveredDate: z.string() })
  .passthrough();
const ReservationSalesCaseDetail = z
  .object({
    salesCaseNumber: z.string(),
    caseType: z.literal("reservation"),
    status: z.string(),
    lots: z.array(z.string()),
    divisionCode: z.number().int(),
    salesDate: z.string(),
    version: z.number().int(),
    reservationPrice: ReservationPrice.nullable(),
    determination: ReservationDetermination.nullable(),
    delivery: ReservationDelivery.nullable(),
  })
  .passthrough();
const Consignor = z
  .object({
    consignorName: z.string(),
    consignorCode: z.string(),
    designatedDate: z.string(),
  })
  .passthrough();
const ConsignmentResult = z
  .object({ resultDate: z.string(), resultAmount: z.number().int() })
  .passthrough();
const ConsignmentSalesCaseDetail = z
  .object({
    salesCaseNumber: z.string(),
    caseType: z.literal("consignment"),
    status: z.string(),
    lots: z.array(z.string()),
    divisionCode: z.number().int(),
    salesDate: z.string(),
    version: z.number().int(),
    consignor: Consignor.nullable(),
    result: ConsignmentResult.nullable(),
  })
  .passthrough();
const SalesCaseDetailResponse = z.discriminatedUnion("caseType", [
  DirectSalesCaseDetail,
  ReservationSalesCaseDetail,
  ConsignmentSalesCaseDetail,
]);
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
    contractAdjustmentRate: z.number().gte(0.9).lte(1.1).nullish(),
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
                    periodAdjustmentRate: z.number().gte(0.9).lte(1.1),
                    counterpartyAdjustmentRate: z.number().gte(0.9).lte(1.1),
                    exceptionalPeriodAdjustmentRate: z
                      .number()
                      .gte(0.9)
                      .lte(1.1)
                      .nullish(),
                  })
                  .passthrough()
              )
              .min(1),
          })
          .passthrough()
      )
      .min(1),
    version: z.number().int().gte(1).lte(2147483647),
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
    version: z.number().int().gte(1).lte(2147483647),
  })
  .passthrough();
const createReservationPrice_Body = z
  .object({
    appraisalDate: z.string(),
    reservedLotInfo: z.string(),
    reservedAmount: z.number().int(),
    version: z.number().int().gte(1).lte(2147483647),
  })
  .passthrough();
const ReservationStatusResponse = z
  .object({ status: z.string(), version: z.number().int() })
  .passthrough();
const confirmReservation_Body = z
  .object({
    determinedDate: z.string(),
    determinedAmount: z.number().int(),
    version: z.number().int().gte(1).lte(2147483647),
  })
  .passthrough();
const deliverReservation_Body = z
  .object({
    deliveryDate: z.string(),
    version: z.number().int().gte(1).lte(2147483647),
  })
  .passthrough();
const designateConsignment_Body = z
  .object({
    consignorName: z.string(),
    consignorCode: z.string(),
    designatedDate: z.string(),
    version: z.number().int().gte(1).lte(2147483647),
  })
  .passthrough();
const ConsignmentStatusResponse = z
  .object({ status: z.string(), version: z.number().int() })
  .passthrough();
const registerConsignmentResult_Body = z
  .object({
    resultDate: z.string(),
    resultAmount: z.number().int(),
    version: z.number().int().gte(1).lte(2147483647),
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
  AvailableLotsResponse,
  CodeMasterItem,
  DepartmentItem,
  SectionItem,
  CodeMastersResponse,
  CodeName,
  LotDetailResponse,
  LotResponse,
  completeManufacturing_Body,
  instructLotShipping_Body,
  instructItemConversion_Body,
  SalesCaseType,
  SalesCaseSummary,
  SalesCasesListResponse,
  createSalesCase_Body,
  CreatedSalesCaseResponse,
  EditCaseLotsRequest,
  DirectAppraisal,
  DirectContract,
  ShippingInstruction,
  ShippingCompletion,
  DirectSalesCaseDetail,
  ReservationPrice,
  ReservationDetermination,
  ReservationDelivery,
  ReservationSalesCaseDetail,
  Consignor,
  ConsignmentResult,
  ConsignmentSalesCaseDetail,
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
        schema: z.string().regex(/^[0-9]{1,9}-[^-\u0000]+-[0-9]{1,9}$/u),
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
    path: "/code-masters",
    alias: "getCodeMasters",
    description: `事業部/部/課（階層）・工程区分/検査区分/製造区分（フラット）のコード値と名称の一覧。
ロット作成フォームのドロップダウンに用いる。&#x60;viewer&#x60; ロール以上で利用可能。
`,
    requestFormat: "json",
    response: CodeMastersResponse,
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
        schema: z.number().int().gte(0).lte(2147483647).optional().default(0),
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
        schema: z.string().regex(/^[0-9]{1,9}-[^-\u0000]+-[0-9]{1,9}$/u),
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
        schema: z
          .object({ version: z.number().int().gte(1).lte(2147483647) })
          .passthrough(),
      },
      {
        name: "id",
        type: "Path",
        schema: z.string().regex(/^[0-9]{1,9}-[^-\u0000]+-[0-9]{1,9}$/u),
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
        schema: z.string().regex(/^[0-9]{1,9}-[^-\u0000]+-[0-9]{1,9}$/u),
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
        schema: z.string().regex(/^[0-9]{1,9}-[^-\u0000]+-[0-9]{1,9}$/u),
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
        schema: z.string().regex(/^[0-9]{1,9}-[^-\u0000]+-[0-9]{1,9}$/u),
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
        schema: z
          .object({ version: z.number().int().gte(1).lte(2147483647) })
          .passthrough(),
      },
      {
        name: "id",
        type: "Path",
        schema: z.string().regex(/^[0-9]{1,9}-[^-\u0000]+-[0-9]{1,9}$/u),
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
        schema: z.string().regex(/^[0-9]{1,9}-[^-\u0000]+-[0-9]{1,9}$/u),
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
    path: "/lots/available",
    alias: "listAvailableLots",
    description: `製造完了かつどの販売案件にも割り当てられていないロットを返す。
&#x60;excludeCase&#x60; を指定すると、その案件に現在割り当て済みのロットも「割当可能」として含める（案件のロット修正用）。
`,
    requestFormat: "json",
    parameters: [
      {
        name: "excludeCase",
        type: "Query",
        schema: z
          .string()
          .regex(/^[0-9]{1,9}-[0-9]{1,9}-[0-9]{1,9}$/)
          .optional(),
      },
    ],
    response: AvailableLotsResponse,
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
        schema: z.string().min(1).optional(),
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
        schema: z.number().int().gte(0).lte(2147483647).optional().default(0),
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
      {
        status: 404,
        description: `リソースなし。RFC 9457 Problem Details 形式`,
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
        schema: z.string().regex(/^[0-9]{1,9}-[0-9]{1,9}-[0-9]{1,9}$/),
      },
    ],
    response: SalesCaseDetailResponse,
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
        schema: z.string().regex(/^[0-9]{1,9}-[0-9]{1,9}-[0-9]{1,9}$/),
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
        schema: z.string().regex(/^[0-9]{1,9}-[0-9]{1,9}-[0-9]{1,9}$/),
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
        schema: z.string().regex(/^[0-9]{1,9}-[0-9]{1,9}-[0-9]{1,9}$/),
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
        schema: z
          .object({ version: z.number().int().gte(1).lte(2147483647) })
          .passthrough(),
      },
      {
        name: "id",
        type: "Path",
        schema: z.string().regex(/^[0-9]{1,9}-[0-9]{1,9}-[0-9]{1,9}$/),
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
        schema: z.string().regex(/^[0-9]{1,9}-[0-9]{1,9}-[0-9]{1,9}$/),
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
        schema: z
          .object({ version: z.number().int().gte(1).lte(2147483647) })
          .passthrough(),
      },
      {
        name: "id",
        type: "Path",
        schema: z.string().regex(/^[0-9]{1,9}-[0-9]{1,9}-[0-9]{1,9}$/),
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
        schema: z.string().regex(/^[0-9]{1,9}-[0-9]{1,9}-[0-9]{1,9}$/),
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
        schema: z.string().regex(/^[0-9]{1,9}-[0-9]{1,9}-[0-9]{1,9}$/),
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
        schema: z
          .object({ version: z.number().int().gte(1).lte(2147483647) })
          .passthrough(),
      },
      {
        name: "id",
        type: "Path",
        schema: z.string().regex(/^[0-9]{1,9}-[0-9]{1,9}-[0-9]{1,9}$/),
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
    method: "put",
    path: "/sales-cases/:id/lots",
    alias: "editCaseLots",
    description: `案件に紐づくロットの集合を total replace する。価格・査定登録前の direct（before_appraisal）
および consignment（before_consignment）のみ許可（reservation は不可）。各ロットは製造完了かつ
他案件に未割当である必要がある（自案件に現在割当済みのロットは可）。&#x60;version&#x60; は楽観的ロック用。
`,
    requestFormat: "json",
    parameters: [
      {
        name: "body",
        type: "Body",
        schema: EditCaseLotsRequest,
      },
      {
        name: "id",
        type: "Path",
        schema: z.string().regex(/^[0-9]{1,9}-[0-9]{1,9}-[0-9]{1,9}$/),
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
        schema: z.string().regex(/^[0-9]{1,9}-[0-9]{1,9}-[0-9]{1,9}$/),
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
        schema: z.string().regex(/^[0-9]{1,9}-[0-9]{1,9}-[0-9]{1,9}$/),
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
        schema: z
          .object({ version: z.number().int().gte(1).lte(2147483647) })
          .passthrough(),
      },
      {
        name: "id",
        type: "Path",
        schema: z.string().regex(/^[0-9]{1,9}-[0-9]{1,9}-[0-9]{1,9}$/),
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
        schema: z.string().regex(/^[0-9]{1,9}-[0-9]{1,9}-[0-9]{1,9}$/),
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
        schema: z.string().regex(/^[0-9]{1,9}-[0-9]{1,9}-[0-9]{1,9}$/),
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
        schema: z.string().regex(/^[0-9]{1,9}-[0-9]{1,9}-[0-9]{1,9}$/),
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
        schema: z
          .object({ version: z.number().int().gte(1).lte(2147483647) })
          .passthrough(),
      },
      {
        name: "id",
        type: "Path",
        schema: z.string().regex(/^[0-9]{1,9}-[0-9]{1,9}-[0-9]{1,9}$/),
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
