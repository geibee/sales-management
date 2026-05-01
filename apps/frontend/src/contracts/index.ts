/**
 * Re-exports the auto-generated Zod schemas plus a small set of hand-written
 * extensions for shapes the OpenAPI spec leaves opaque.
 *
 * Auto-generated (`gen:api` from openapi.yaml) — see ./generated.ts `schemas`:
 *   - LotStatus, LotResponse, CreateLotResponse
 *   - completeManufacturing_Body, instructLotShipping_Body, instructItemConversion_Body
 *   - createSalesCase_Body, CreatedSalesCaseResponse, SalesCaseDetailResponse, ...
 *   - PriceCheckResponse
 *
 * Hand-written:
 *   - ProblemJson (RFC 9457 — yaml has it inline; openapi-zod-client doesn't
 *     surface a named export for it)
 *   - DateOnly (form-level helper)
 */
import { z } from "zod";
import { schemas } from "./generated";

export { schemas, api, createApiClient } from "./generated";

// Re-export commonly used generated schemas with friendlier names.
export const LotResponseSchema = schemas.LotResponse;
export const LotStatusSchema = schemas.LotStatus;
export const CreateLotResponseSchema = schemas.CreateLotResponse;
export const LotsListResponseSchema = schemas.LotsListResponse;
export const PriceCheckResponseSchema = schemas.PriceCheckResponse;
export const CreatedSalesCaseResponseSchema = schemas.CreatedSalesCaseResponse;
export const SalesCasesListResponseSchema = schemas.SalesCasesListResponse;
export const SalesCaseTypeSchema = schemas.SalesCaseType;
export const SalesCaseDetailResponseSchema = schemas.SalesCaseDetailResponse;
export type LotResponse = z.infer<typeof schemas.LotResponse>;
export type LotStatus = z.infer<typeof schemas.LotStatus>;
export type CreateLotResponse = z.infer<typeof schemas.CreateLotResponse>;
export type LotsListResponse = z.infer<typeof schemas.LotsListResponse>;
export type PriceCheckResponse = z.infer<typeof schemas.PriceCheckResponse>;
export type CreatedSalesCaseResponse = z.infer<typeof schemas.CreatedSalesCaseResponse>;
export type SalesCasesListResponse = z.infer<typeof schemas.SalesCasesListResponse>;
export type SalesCaseType = z.infer<typeof schemas.SalesCaseType>;
export type SalesCaseDetailResponse = z.infer<typeof schemas.SalesCaseDetailResponse>;

// ---- Hand-written extensions ----

export const ProblemJsonSchema = z.object({
  type: z.string().optional(),
  title: z.string().optional(),
  status: z.number().int(),
  detail: z.string().optional(),
});
export type ProblemJson = z.infer<typeof ProblemJsonSchema>;

/**
 * Date input helper (yyyy-MM-dd).
 */
export const DateOnlySchema = z
  .string()
  .regex(/^\d{4}-\d{2}-\d{2}$/, "yyyy-MM-dd 形式で入力してください");
