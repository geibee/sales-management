import { type SalesCaseDetailResponse, SalesCaseDetailResponseSchema } from "@/contracts";
import { ApiError, apiGet, apiSend } from "@/lib/api-client";
import useSWR, { mutate as globalMutate } from "swr";

export function reservationCaseKey(id: string): string {
  // Reservation cases share the polymorphic /sales-cases/{id} read endpoint.
  return `/sales-cases/${id}`;
}

export function useReservationCase(id: string | null) {
  return useSWR<SalesCaseDetailResponse>(id ? reservationCaseKey(id) : null, (key: string) =>
    apiGet(key, SalesCaseDetailResponseSchema),
  );
}

export async function refreshReservationCase(id: string): Promise<void> {
  await globalMutate(reservationCaseKey(id));
}

async function withConflictRefresh<T>(id: string, fn: () => Promise<T>): Promise<T> {
  try {
    const result = await fn();
    await refreshReservationCase(id);
    return result;
  } catch (e) {
    if (e instanceof ApiError && e.status === 409) {
      await refreshReservationCase(id);
    }
    throw e;
  }
}

type EstimateBody = Record<string, unknown>;

export async function createReservationPrice(id: string, body: EstimateBody): Promise<void> {
  await withConflictRefresh(id, () =>
    apiSend("POST", `/sales-cases/${id}/reservation/appraisals`, body),
  );
}

export async function confirmReservation(id: string, body: EstimateBody): Promise<void> {
  await withConflictRefresh(id, () =>
    apiSend("POST", `/sales-cases/${id}/reservation/determine`, body),
  );
}

export async function cancelReservationConfirmation(id: string, version: number): Promise<void> {
  await withConflictRefresh(id, () =>
    apiSend("DELETE", `/sales-cases/${id}/reservation/determination`, { version }),
  );
}

export async function deliverReservation(id: string, body: EstimateBody): Promise<void> {
  await withConflictRefresh(id, () =>
    apiSend("POST", `/sales-cases/${id}/reservation/delivery`, body),
  );
}
