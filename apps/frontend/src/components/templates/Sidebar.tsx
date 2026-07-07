import { RoleBadge } from "@/components/templates/RoleBadge";
import { useLotsList } from "@/hooks/use-lots-list";
import { useSalesCasesList } from "@/hooks/use-sales-cases-list";
import { useAuth } from "@/stores/auth-store";
import { Link } from "@tanstack/react-router";
import { Globe, Home, Package, Plus, Receipt, Settings } from "lucide-react";
import type { ComponentType } from "react";

type NavItem = {
  label: string;
  to: string;
  icon: ComponentType<{ className?: string }>;
  count?: number | undefined;
  /** routes that should also light this item up */
  matchPrefix?: string | undefined;
};

type NavGroup = { group: string; items: NavItem[] };

export function Sidebar() {
  const { data: lots } = useLotsList({ limit: 1 });
  const { data: cases } = useSalesCasesList({ limit: 1 });
  const roles = useAuth((s) => s.roles);
  const userInitial = roles.has("admin") ? "AD" : roles.has("operator") ? "OP" : "VW";

  const groups: NavGroup[] = [
    {
      group: "メイン",
      items: [{ label: "ダッシュボード", to: "/", icon: Home }],
    },
    {
      group: "在庫管理",
      items: [
        {
          label: "在庫ロット",
          to: "/lots",
          icon: Package,
          count: lots?.total,
          matchPrefix: "/lots",
        },
        { label: "ロットを作成", to: "/lots/new", icon: Plus },
      ],
    },
    {
      group: "販売管理",
      items: [
        {
          label: "販売案件",
          to: "/sales-cases",
          icon: Receipt,
          count: cases?.total,
          matchPrefix: "/sales-cases",
        },
        { label: "案件を作成", to: "/sales-cases/new", icon: Plus },
      ],
    },
    {
      group: "外部連携",
      items: [{ label: "外部価格チェック", to: "/external/price-check", icon: Globe }],
    },
  ];

  return (
    <aside className="rail">
      <div className="rail-brand">
        <div className="rail-mark">SM</div>
        <div className="rail-brand-text">
          <span className="t1">Sales Management</span>
          <span className="t2">admin</span>
        </div>
      </div>

      <nav className="rail-nav">
        {groups.map((g) => (
          <div key={g.group} className="mb-2">
            <div className="rail-group">{g.group}</div>
            {g.items.map((it) => {
              const Icon = it.icon;
              const isList = it.to === "/lots" || it.to === "/sales-cases";
              return (
                <Link
                  key={it.to}
                  to={it.to}
                  className="rail-link"
                  activeProps={{ className: "rail-link active" }}
                  activeOptions={{ exact: it.to === "/" ? true : !isList }}
                >
                  <Icon className="ico" />
                  <span>{it.label}</span>
                  {it.count != null && <span className="badge-count">{it.count}</span>}
                </Link>
              );
            })}
          </div>
        ))}
      </nav>

      <div className="rail-footer">
        <div className="rail-user">{userInitial}</div>
        <div className="rail-user-meta">
          <div className="n">オペレーター</div>
          <div className="r">
            <RoleBadge />
          </div>
        </div>
        <button type="button" className="icon-btn" title="設定" aria-label="設定">
          <Settings size={15} />
        </button>
      </div>
    </aside>
  );
}
