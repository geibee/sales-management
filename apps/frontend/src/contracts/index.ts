/**
 * Re-exports the auto-generated Zod schemas plus ProblemJson (which openapi-zod-client
 * does not surface as a named export since it only appears in error responses).
 * Form-level validators live in src/forms/validators.ts.
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
export const DirectSalesCaseDetailSchema = schemas.DirectSalesCaseDetail;
export const ReservationSalesCaseDetailSchema = schemas.ReservationSalesCaseDetail;
export const ConsignmentSalesCaseDetailSchema = schemas.ConsignmentSalesCaseDetail;
export type LotResponse = z.infer<typeof schemas.LotResponse>;
export type LotStatus = z.infer<typeof schemas.LotStatus>;
export type CreateLotResponse = z.infer<typeof schemas.CreateLotResponse>;
export type LotsListResponse = z.infer<typeof schemas.LotsListResponse>;
export type PriceCheckResponse = z.infer<typeof schemas.PriceCheckResponse>;
export type CreatedSalesCaseResponse = z.infer<typeof schemas.CreatedSalesCaseResponse>;
export type SalesCasesListResponse = z.infer<typeof schemas.SalesCasesListResponse>;
export type SalesCaseType = z.infer<typeof schemas.SalesCaseType>;
export type SalesCaseDetailResponse = z.infer<typeof schemas.SalesCaseDetailResponse>;
export type DirectSalesCaseDetail = z.infer<typeof schemas.DirectSalesCaseDetail>;
export type ReservationSalesCaseDetail = z.infer<typeof schemas.ReservationSalesCaseDetail>;
export type ConsignmentSalesCaseDetail = z.infer<typeof schemas.ConsignmentSalesCaseDetail>;
export const CodeMastersResponseSchema = schemas.CodeMastersResponse;
export const AvailableLotsResponseSchema = schemas.AvailableLotsResponse;
export type CodeMastersResponse = z.infer<typeof schemas.CodeMastersResponse>;
export type AvailableLotsResponse = z.infer<typeof schemas.AvailableLotsResponse>;

// ---- Hand-written extensions ----

export const ProblemJsonSchema = z.object({
  type: z.string().optional(),
  title: z.string().optional(),
  status: z.number().int(),
  detail: z.string().optional(),
});
export type ProblemJson = z.infer<typeof ProblemJsonSchema>;
