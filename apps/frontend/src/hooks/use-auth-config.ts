import { schemas } from "@/contracts";
import { apiGet } from "@/lib/api-client";
import useSWR from "swr";
import type { z } from "zod";

export type AuthConfig = z.infer<typeof schemas.AuthConfigResponse>;

export const AUTH_CONFIG_KEY = "/auth/config";

/**
 * Fetch the public `/auth/config` endpoint and cache it via SWR.
 *
 * The backend returns `{ enabled: boolean, authority?: string, audience?: string }`.
 * When `enabled === false` the SPA may bypass `<Guard>` checks; otherwise the
 * frontend must rely on the JWT in `auth-store` for role decisions.
 *
 * Auth-config is treated as effectively immutable for the lifetime of the SPA,
 * so revalidation on focus / reconnect is disabled.
 */
export function useAuthConfig() {
  return useSWR<AuthConfig>(
    AUTH_CONFIG_KEY,
    (key: string) => apiGet(key, schemas.AuthConfigResponse),
    {
      revalidateOnFocus: false,
      revalidateOnReconnect: false,
      revalidateIfStale: false,
      shouldRetryOnError: false,
    },
  );
}
