import { type PriceCheckResponse, PriceCheckResponseSchema } from "@/contracts";
import { apiGet } from "@/lib/api-client";
import useSWR from "swr";

/**
 * The external pricing endpoint is rate-limited and circuit-broken upstream.
 * Disable automatic revalidation; the page triggers fetches explicitly via
 * `mutate()`.
 *
 * `lotId` is required by the backend (returns 400 otherwise). The hook
 * stays disabled (`null` key) until the caller passes a non-empty value.
 */
export function useExternalPriceCheck(lotId: string | null) {
  const key = lotId ? `/api/external/price-check?lotId=${encodeURIComponent(lotId)}` : null;
  return useSWR<PriceCheckResponse>(key, (k) => apiGet(k, PriceCheckResponseSchema), {
    revalidateOnFocus: false,
    revalidateOnReconnect: false,
    revalidateOnMount: true,
    shouldRetryOnError: false,
  });
}
