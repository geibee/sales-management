/**
 * Re-exports the auto-generated Zod schemas plus ProblemJson.
 *
 * ProblemJson (RFC 9457): openapi.yaml には `components.schemas.ProblemDetails` として
 * named schema で書かれているが、すべての参照が `application/problem+json` メディアタイプ
 * 経由のため、`application/json` しか拾わない openapi-zod-client は generated.ts に
 * 出力しない。フロントが error body を parse する用に同じ shape を手書きで再現している。
 *
 * Schemas は generated.ts の `schemas.X` を直接参照する。型だけは `z.infer<typeof schemas.X>`
 * のショートカットとして alias を提供する。Form-level validators は src/forms/validators.ts。
 */
import { z } from "zod";
import type { schemas } from "./generated";

export { schemas, api, createApiClient } from "./generated";

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
