import {
  type CreateLotResponse,
  CreateLotResponseSchema,
  type LotResponse,
  LotResponseSchema,
} from "@/contracts";
import { ApiError, apiDownload, apiGet, apiSend } from "@/lib/api-client";
import useSWR, { mutate as globalMutate } from "swr";

export function useLot(id: string | null) {
  return useSWR<LotResponse>(id ? `/lots/${id}` : null, (key: string) =>
    apiGet(key, LotResponseSchema),
  );
}

export function lotKey(id: string): string {
  return `/lots/${id}`;
}

export async function refreshLot(id: string): Promise<void> {
  await globalMutate(lotKey(id));
}

/**
 * Run a Lot-mutating call. On 409 Conflict (optimistic-lock failure),
 * refetch the lot before propagating the error so the UI shows the
 * latest version + the caller's catch can surface a friendly message.
 */
async function withConflictRefresh<T>(id: string, fn: () => Promise<T>): Promise<T> {
  try {
    const result = await fn();
    await refreshLot(id);
    return result;
  } catch (e) {
    if (e instanceof ApiError && e.status === 409) {
      await refreshLot(id);
    }
    throw e;
  }
}

// ---- Lot creation ----
type LotCreateBody = {
  lotNumber: { year: number; location: string; seq: number };
  divisionCode: number;
  departmentCode: number;
  sectionCode: number;
  processCategory: number;
  inspectionCategory: number;
  manufacturingCategory: number;
  details: Array<{
    itemCategory: string;
    premiumCategory: string;
    productCategoryCode: string;
    lengthSpecLower: number;
    thicknessSpecLower: number;
    thicknessSpecUpper: number;
    qualityGrade: string;
    count: number;
    quantity: number;
    inspectionResultCategory: string;
  }>;
};

export async function createLot(body: LotCreateBody): Promise<CreateLotResponse> {
  return await apiSend("POST", "/lots", body, CreateLotResponseSchema);
}

// ---- Lot state transitions ----
export async function completeManufacturing(
  id: string,
  date: string,
  version: number,
): Promise<LotResponse> {
  return await withConflictRefresh(id, () =>
    apiSend("POST", `/lots/${id}/complete-manufacturing`, { date, version }, LotResponseSchema),
  );
}

export async function cancelManufacturingCompletion(
  id: string,
  version: number,
): Promise<LotResponse> {
  return await withConflictRefresh(id, () =>
    apiSend("POST", `/lots/${id}/cancel-manufacturing-completion`, { version }, LotResponseSchema),
  );
}

export async function instructShipping(
  id: string,
  deadline: string,
  version: number,
): Promise<LotResponse> {
  return await withConflictRefresh(id, () =>
    apiSend("POST", `/lots/${id}/instruct-shipping`, { deadline, version }, LotResponseSchema),
  );
}

export async function completeShipping(
  id: string,
  date: string,
  version: number,
): Promise<LotResponse> {
  return await withConflictRefresh(id, () =>
    apiSend("POST", `/lots/${id}/complete-shipping`, { date, version }, LotResponseSchema),
  );
}

export async function instructItemConversion(
  id: string,
  destinationItem: string,
  version: number,
): Promise<LotResponse> {
  return await withConflictRefresh(id, () =>
    apiSend(
      "POST",
      `/lots/${id}/instruct-item-conversion`,
      { destinationItem, version },
      LotResponseSchema,
    ),
  );
}

export async function cancelItemConversionInstruction(
  id: string,
  version: number,
): Promise<LotResponse> {
  return await withConflictRefresh(id, () =>
    apiSend("DELETE", `/lots/${id}/instruct-item-conversion`, { version }, LotResponseSchema),
  );
}

// ---- CSV Export ----
export async function exportLotsCsv(status?: string): Promise<void> {
  const qs = status && status !== "all" ? `?status=${encodeURIComponent(status)}` : "";
  const date = new Date().toISOString().slice(0, 10).replace(/-/g, "");
  await apiDownload(`/lots/export${qs}`, `lots_${date}.csv`);
}
