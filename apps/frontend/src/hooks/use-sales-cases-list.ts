import { type SalesCasesListResponse, SalesCasesListResponseSchema } from "@/contracts";
import { apiGet } from "@/lib/api-client";
import useSWR from "swr";

export type SalesCasesListQuery = {
  status?: string;
  caseType?: string;
  limit?: number;
  offset?: number;
};

function buildPath(q: SalesCasesListQuery): string {
  const params = new URLSearchParams();
  if (q.status && q.status !== "all") params.set("status", q.status);
  if (q.caseType && q.caseType !== "all") params.set("caseType", q.caseType);
  if (q.limit != null) params.set("limit", String(q.limit));
  if (q.offset != null) params.set("offset", String(q.offset));
  const qs = params.toString();
  return qs ? `/sales-cases?${qs}` : "/sales-cases";
}

export function useSalesCasesList(q: SalesCasesListQuery) {
  const key = buildPath(q);
  return useSWR<SalesCasesListResponse>(
    key,
    (k: string) => apiGet(k, SalesCasesListResponseSchema),
    {
      keepPreviousData: true,
    },
  );
}
