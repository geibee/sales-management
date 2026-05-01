import { Badge } from "@/components/ui/badge";
import { useAuth } from "@/stores/auth-store";

export function RoleBadge() {
  const roles = useAuth((s) => s.roles);
  const token = useAuth((s) => s.token);
  if (!token) return <Badge variant="outline">未認証</Badge>;
  if (roles.size === 0) return <Badge variant="secondary">no-role</Badge>;
  const top = roles.has("admin")
    ? "admin"
    : roles.has("operator")
      ? "operator"
      : "viewer";
  return <Badge>{top}</Badge>;
}
