import { type SalesCaseDetailResponse, SalesCaseDetailResponseSchema } from "@/contracts";
import { ApiError, apiGet, apiSend } from "@/lib/api-client";
import useSWR, { mutate as globalMutate } from "swr";

export function consignmentCaseKey(id: string): string {
  return `/sales-cases/${id}`;
}

export function useConsignmentCase(id: string | null) {
  return useSWR<SalesCaseDetailResponse>(id ? consignmentCaseKey(id) : null, (key: string) =>
    apiGet(key, SalesCaseDetailResponseSchema),
  );
}

export async function refreshConsignmentCase(id: string): Promise<void> {
  await globalMutate(consignmentCaseKey(id));
}

async function withConflictRefresh<T>(id: string, fn: () => Promise<T>): Promise<T> {
  try {
    const result = await fn();
    await refreshConsignmentCase(id);
    return result;
  } catch (e) {
    if (e instanceof ApiError && e.status === 409) {
      await refreshConsignmentCase(id);
    }
    throw e;
  }
}

type ConsignmentBody = Record<string, unknown>;

export async function designateConsignment(id: string, body: ConsignmentBody): Promise<void> {
  await withConflictRefresh(id, () =>
    apiSend("POST", `/sales-cases/${id}/consignment/designate`, body),
  );
}

export async function cancelDesignation(id: string, version: number): Promise<void> {
  await withConflictRefresh(id, () =>
    apiSend("DELETE", `/sales-cases/${id}/consignment/designation`, { version }),
  );
}

export async function recordConsignmentResult(id: string, body: ConsignmentBody): Promise<void> {
  await withConflictRefresh(id, () =>
    apiSend("POST", `/sales-cases/${id}/consignment/result`, body),
  );
}
