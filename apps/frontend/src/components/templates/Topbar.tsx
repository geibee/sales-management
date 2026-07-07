import { HealthIndicator } from "@/components/templates/HealthIndicator";
import { SwaggerLink } from "@/components/templates/SwaggerLink";
import { Link, useRouterState } from "@tanstack/react-router";
import { Bell, Home, Search } from "lucide-react";
import { Fragment } from "react";

type Crumb = { label: string; to?: string; mono?: boolean };

function crumbsFor(pathname: string): Crumb[] {
  const seg = pathname.split("/").filter(Boolean);
  if (seg.length === 0) return [{ label: "ダッシュボード" }];

  if (seg[0] === "lots") {
    const base: Crumb[] = [{ label: "在庫管理" }];
    if (seg.length === 1) return [...base, { label: "在庫ロット" }];
    if (seg[1] === "new")
      return [...base, { label: "在庫ロット", to: "/lots" }, { label: "新規作成" }];
    return [...base, { label: "在庫ロット", to: "/lots" }, { label: seg[1] ?? "", mono: true }];
  }

  if (
    seg[0] === "sales-cases" ||
    seg[0] === "reservation-cases" ||
    seg[0] === "consignment-cases"
  ) {
    const base: Crumb[] = [{ label: "販売管理" }];
    if (seg[0] === "sales-cases" && seg.length === 1) return [...base, { label: "販売案件" }];
    if (seg[0] === "sales-cases" && seg[1] === "new")
      return [...base, { label: "販売案件", to: "/sales-cases" }, { label: "新規作成" }];
    return [
      ...base,
      { label: "販売案件", to: "/sales-cases" },
      { label: seg[1] ?? "", mono: true },
    ];
  }

  if (seg[0] === "external") {
    return [{ label: "外部連携" }, { label: "外部価格チェック" }];
  }

  return [{ label: "ダッシュボード" }];
}

export function Topbar() {
  const pathname = useRouterState({ select: (s) => s.location.pathname });
  const crumbs = crumbsFor(pathname);

  return (
    <div className="topbar">
      <nav className="crumbs" aria-label="パンくず">
        <Link to="/" aria-label="ホーム">
          <Home size={14} style={{ verticalAlign: -2, marginRight: 4 }} />
        </Link>
        <span className="sep">/</span>
        {crumbs.map((c, i) => {
          const last = i === crumbs.length - 1;
          return (
            <Fragment key={`${c.label}-${i}`}>
              {c.to ? (
                <Link to={c.to} className={c.mono ? "mono" : undefined}>
                  {c.label}
                </Link>
              ) : (
                <span className={`${last ? "leaf" : ""} ${c.mono ? "mono" : ""}`.trim()}>
                  {c.label}
                </span>
              )}
              {!last && <span className="sep">/</span>}
            </Fragment>
          );
        })}
      </nav>

      <div className="topbar-spacer" />

      <div className="search-box" aria-hidden>
        <Search size={14} />
        <span>ロット・案件を検索…</span>
        <span className="kbd">⌘K</span>
      </div>

      <HealthIndicator />

      <button type="button" className="icon-btn" title="通知" aria-label="通知">
        <Bell size={15} />
      </button>

      <SwaggerLink />
    </div>
  );
}
