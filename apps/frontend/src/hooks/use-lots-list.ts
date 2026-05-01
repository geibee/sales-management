import { type LotsListResponse, LotsListResponseSchema } from "@/contracts";
import { apiGet } from "@/lib/api-client";
import useSWR from "swr";

export type LotsListQuery = {
  status?: string;
  limit?: number;
  offset?: number;
};

function buildPath(q: LotsListQuery): string {
  const params = new URLSearchParams();
  if (q.status && q.status !== "all") params.set("status", q.status);
  if (q.limit != null) params.set("limit", String(q.limit));
  if (q.offset != null) params.set("offset", String(q.offset));
  const qs = params.toString();
  return qs ? `/lots?${qs}` : "/lots";
}

export function useLotsList(q: LotsListQuery) {
  const key = buildPath(q);
  return useSWR<LotsListResponse>(key, (k: string) => apiGet(k, LotsListResponseSchema), {
    keepPreviousData: true,
  });
}
