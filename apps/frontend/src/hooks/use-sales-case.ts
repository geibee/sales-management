import {
  type CreatedSalesCaseResponse,
  CreatedSalesCaseResponseSchema,
  type SalesCaseDetailResponse,
  SalesCaseDetailResponseSchema,
} from "@/contracts";
import { ApiError, apiGet, apiSend } from "@/lib/api-client";
import useSWR, { mutate as globalMutate } from "swr";

export function salesCaseKey(id: string): string {
  return `/sales-cases/${id}`;
}

/**
 * GET /sales-cases/{id} returns a polymorphic detail body keyed by `caseType`.
 * Used by all three case-detail pages (direct / reservation / consignment).
 */
export function useSalesCase(id: string | null) {
  return useSWR<SalesCaseDetailResponse>(id ? salesCaseKey(id) : null, (key: string) =>
    apiGet(key, SalesCaseDetailResponseSchema),
  );
}

export async function refreshSalesCase(id: string): Promise<void> {
  await globalMutate(salesCaseKey(id));
}

async function withConflictRefresh<T>(id: string, fn: () => Promise<T>): Promise<T> {
  try {
    const result = await fn();
    await refreshSalesCase(id);
    return result;
  } catch (e) {
    if (e instanceof ApiError && e.status === 409) {
      await refreshSalesCase(id);
    }
    throw e;
  }
}

// ---- Create / Delete ----
type SalesCaseCreateBody = {
  lots: string[];
  divisionCode: number;
  salesDate: string;
  caseType: "direct" | "reservation" | "consignment";
};

export async function createSalesCase(
  body: SalesCaseCreateBody,
): Promise<CreatedSalesCaseResponse> {
  return await apiSend("POST", "/sales-cases", body, CreatedSalesCaseResponseSchema);
}

export async function deleteSalesCase(id: string): Promise<void> {
  await apiSend("DELETE", salesCaseKey(id));
}

// ---- Appraisal ----
type AppraisalBody = Record<string, unknown>;

export async function createAppraisal(id: string, body: AppraisalBody): Promise<void> {
  await withConflictRefresh(id, () => apiSend("POST", `/sales-cases/${id}/appraisals`, body));
}

export async function updateAppraisal(id: string, body: AppraisalBody): Promise<void> {
  await withConflictRefresh(id, () => apiSend("PUT", `/sales-cases/${id}/appraisals`, body));
}

export async function deleteAppraisal(id: string, version: number): Promise<void> {
  await withConflictRefresh(id, () =>
    apiSend("DELETE", `/sales-cases/${id}/appraisals`, { version }),
  );
}

// ---- Contract ----
export async function createContract(id: string, body: AppraisalBody): Promise<void> {
  await withConflictRefresh(id, () => apiSend("POST", `/sales-cases/${id}/contracts`, body));
}

export async function deleteContract(id: string, version: number): Promise<void> {
  await withConflictRefresh(id, () =>
    apiSend("DELETE", `/sales-cases/${id}/contracts`, { version }),
  );
}

// ---- Shipping ----
export async function instructSalesShipping(id: string, body: AppraisalBody): Promise<void> {
  await withConflictRefresh(id, () =>
    apiSend("POST", `/sales-cases/${id}/shipping-instruction`, body),
  );
}

export async function cancelSalesShippingInstruction(id: string, version: number): Promise<void> {
  await withConflictRefresh(id, () =>
    apiSend("DELETE", `/sales-cases/${id}/shipping-instruction`, { version }),
  );
}

export async function completeSalesShipping(id: string, body: AppraisalBody): Promise<void> {
  await withConflictRefresh(id, () =>
    apiSend("POST", `/sales-cases/${id}/shipping-completion`, body),
  );
}
