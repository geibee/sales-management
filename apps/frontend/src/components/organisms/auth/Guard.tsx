import { useAuthConfig } from "@/hooks/use-auth-config";
import { type Role, useAuth } from "@/stores/auth-store";
import type { ReactNode } from "react";

/**
 * Conditionally render `children` based on whether the current user satisfies
 * the required role. Roles are hierarchical: admin ⊃ operator ⊃ viewer.
 *
 * Bypass logic is driven by the backend `/auth/config` response:
 *   - `enabled === false` → guard is permissive (auth is OFF on the server)
 *   - `enabled === true`  → guard checks roles from the JWT in auth-store
 *
 * Until `/auth/config` resolves we render the fallback to avoid flashing
 * privileged UI to unauthenticated visitors.
 */
export function Guard({
  requiredRole,
  children,
  fallback = null,
}: {
  requiredRole: Role;
  children: ReactNode;
  fallback?: ReactNode;
}) {
  const { data: cfg, isLoading } = useAuthConfig();
  const hasRole = useAuth((s) => s.hasRole(requiredRole));
  if (isLoading || !cfg) return <>{fallback}</>;
  const ok = cfg.enabled === false ? true : hasRole;
  return <>{ok ? children : fallback}</>;
}
