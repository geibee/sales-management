import { apiGet } from "@/lib/api-client";
import useSWR from "swr";
import { z } from "zod";

const HealthSchema = z.object({}).passthrough();

export function HealthIndicator() {
  const { data, error } = useSWR(
    "/health",
    (k) =>
      apiGet(k, HealthSchema)
        .then(() => true)
        .catch(() => false),
    { refreshInterval: 30_000 },
  );
  const ok = data === true && !error;
  return (
    <span
      className="inline-flex items-center gap-1.5 text-xs"
      title={ok ? "API 稼働中" : "API 応答なし"}
    >
      <span
        className={`inline-block size-2 rounded-full ${ok ? "bg-emerald-500" : "bg-red-500"}`}
        aria-hidden
      />
      <span className="text-muted-foreground">{ok ? "OK" : "DOWN"}</span>
    </span>
  );
}
