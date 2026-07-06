import { type AvailableLotsResponse, schemas } from "@/contracts";
import { apiGet } from "@/lib/api-client";
import useSWR from "swr";

export type AvailableLotsQuery = {
  /** 除外する販売案件番号。指定時はその案件に現在割当済みのロットも候補に含める（修正用）。 */
  excludeCase?: string;
  /** false のときは fetch しない（モーダルが閉じている間など）。 */
  enabled?: boolean;
};

export function useAvailableLots(q: AvailableLotsQuery = {}) {
  const enabled = q.enabled ?? true;
  const key = !enabled
    ? null
    : q.excludeCase
      ? `/lots/available?excludeCase=${encodeURIComponent(q.excludeCase)}`
      : "/lots/available";

  return useSWR<AvailableLotsResponse>(key, (k: string) =>
    apiGet(k, schemas.AvailableLotsResponse),
  );
}
