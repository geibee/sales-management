import { Link } from "@tanstack/react-router";
import { HealthIndicator } from "./HealthIndicator";
import { RoleBadge } from "./RoleBadge";
import { SwaggerLink } from "./SwaggerLink";

const navItems = [
  { label: "ホーム", to: "/" },
  { label: "ロット一覧", to: "/lots" },
  { label: "販売案件一覧", to: "/sales-cases" },
  { label: "外部価格チェック", to: "/external/price-check" },
] as const;

export function Header() {
  return (
    <header className="border-b bg-card">
      <div className="mx-auto flex max-w-screen-xl items-center gap-4 px-4 py-3">
        <Link to="/" className="text-lg font-semibold">
          Sales Management Admin
        </Link>
        <nav className="flex flex-1 items-center gap-3">
          {navItems.slice(1).map((item) => (
            <Link
              key={item.to}
              to={item.to}
              className="text-muted-foreground text-sm hover:text-foreground"
              activeProps={{ className: "text-foreground font-medium" }}
            >
              {item.label}
            </Link>
          ))}
        </nav>
        <div className="flex items-center gap-3">
          <HealthIndicator />
          <SwaggerLink />
          <RoleBadge />
        </div>
      </div>
    </header>
  );
}
