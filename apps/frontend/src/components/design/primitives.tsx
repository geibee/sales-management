import {
  type StatusTone,
  caseStatusLabel,
  caseStatusTone,
  caseTypeLabel,
  lotStatusLabel,
  lotStatusTone,
} from "@/lib/format";
/**
 * Design-system primitives for the redesigned admin UI.
 *
 * These render the plain semantic classes defined in `styles.css`
 * (`.pill`, `.kpi`, `.flow`, `.card-d`, …) rather than Tailwind utilities,
 * so the visual output matches the Claude Design prototype 1:1. Icons come
 * from `lucide-react` (already a dependency).
 */
import { cn } from "@/lib/utils";
import { ArrowDown, ArrowUp, Check } from "lucide-react";
import type { HTMLAttributes, ReactNode } from "react";

/* ---------- Pill ---------- */
export function Pill({
  tone = "neutral",
  dot = false,
  mono = false,
  className,
  children,
}: {
  tone?: StatusTone;
  dot?: boolean;
  mono?: boolean;
  className?: string;
  children: ReactNode;
}) {
  return (
    <span className={cn("pill", `pill-${tone}`, mono && "pill-mono", className)}>
      {dot && <span className="dot" />}
      {children}
    </span>
  );
}

export function LotStatusPill({ status }: { status: string | null | undefined }) {
  return (
    <Pill tone={lotStatusTone(status)} dot>
      {lotStatusLabel(status)}
    </Pill>
  );
}

export function CaseStatusPill({
  caseType,
  status,
}: {
  caseType: string | null | undefined;
  status: string | null | undefined;
}) {
  return (
    <Pill tone={caseStatusTone(status)} dot>
      {caseStatusLabel(caseType, status)}
    </Pill>
  );
}

export function CaseTypePill({ caseType }: { caseType: string | null | undefined }) {
  return <Pill tone="outline">{caseTypeLabel(caseType)}</Pill>;
}

/* ---------- Card (design) ---------- */
export function DCard({
  className,
  children,
  ...rest
}: { className?: string; children: ReactNode } & Omit<
  HTMLAttributes<HTMLDivElement>,
  "className" | "children"
>) {
  return (
    <div className={cn("card-d", className)} {...rest}>
      {children}
    </div>
  );
}

export function DCardHeader({
  title,
  icon,
  actions,
}: {
  title: ReactNode;
  icon?: ReactNode;
  actions?: ReactNode;
}) {
  return (
    <div className="card-header-d">
      <div className="card-title-d">
        {icon}
        <span>{title}</span>
      </div>
      {actions && <div className="row gap-2">{actions}</div>}
    </div>
  );
}

export function DCardBody({
  tight = false,
  flush = false,
  className,
  children,
}: {
  tight?: boolean;
  flush?: boolean;
  className?: string;
  children: ReactNode;
}) {
  return (
    <div className={cn("card-body-d", tight && "tight", flush && "flush", className)}>
      {children}
    </div>
  );
}

/* ---------- Sparkline ---------- */
export function Sparkline({
  data,
  width = 80,
  height = 24,
  tone = "accent",
  filled = true,
}: {
  data: number[];
  width?: number;
  height?: number;
  tone?: "accent" | "ok" | "warn" | "neutral";
  filled?: boolean;
}) {
  if (!data || data.length === 0) return null;
  const min = Math.min(...data);
  const max = Math.max(...data);
  const span = max - min || 1;
  const stepX = width / (data.length - 1);
  const pts = data.map(
    (v, i) => [i * stepX, height - 4 - ((v - min) / span) * (height - 8)] as const,
  );
  const d = pts.map((p, i) => (i === 0 ? `M${p[0]},${p[1]}` : `L${p[0]},${p[1]}`)).join(" ");
  const area = `${d} L${width},${height} L0,${height} Z`;
  const stroke =
    tone === "accent"
      ? "var(--accent-design)"
      : tone === "ok"
        ? "var(--ok)"
        : tone === "warn"
          ? "var(--warn)"
          : "var(--fg-muted)";
  const fill =
    tone === "accent"
      ? "var(--accent-soft)"
      : tone === "ok"
        ? "var(--ok-soft)"
        : tone === "warn"
          ? "var(--warn-soft)"
          : "var(--bg-sunk)";
  const last = pts[pts.length - 1];
  return (
    <svg
      className="spark"
      width={width}
      height={height}
      viewBox={`0 0 ${width} ${height}`}
      role="img"
    >
      <title>推移スパークライン</title>
      {filled && <path d={area} fill={fill} />}
      <path
        d={d}
        fill="none"
        stroke={stroke}
        strokeWidth="1.5"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
      <circle cx={last[0]} cy={last[1]} r="2" fill={stroke} />
    </svg>
  );
}

/* ---------- KPI tile ---------- */
export function KPI({
  label,
  value,
  unit,
  icon,
  delta,
  deltaTone = "up",
  spark,
  sparkTone = "accent",
}: {
  label: ReactNode;
  value: ReactNode;
  unit?: ReactNode;
  icon?: ReactNode;
  delta?: ReactNode;
  deltaTone?: "up" | "down" | "flat";
  spark?: number[];
  sparkTone?: "accent" | "ok" | "warn" | "neutral";
}) {
  return (
    <div className="kpi">
      <div className="kpi-label">
        {icon}
        {label}
      </div>
      <div className="kpi-value tnum">
        {value}
        {unit && <span className="unit">{unit}</span>}
      </div>
      <div className="kpi-foot">
        {delta != null && (
          <span className={`kpi-delta ${deltaTone}`}>
            {deltaTone === "up" ? (
              <ArrowUp className="ico" size={11} />
            ) : deltaTone === "down" ? (
              <ArrowDown className="ico" size={11} />
            ) : null}
            {delta}
          </span>
        )}
        {spark && <Sparkline data={spark} tone={sparkTone} />}
      </div>
    </div>
  );
}

/* ---------- Page header (design) ---------- */
export function DesignPageHeader({
  eyebrow,
  title,
  subtitle,
  actions,
}: {
  eyebrow?: ReactNode;
  title: ReactNode;
  subtitle?: ReactNode;
  actions?: ReactNode;
}) {
  return (
    <div className="page-header">
      <div className="page-title-row">
        {eyebrow && <div className="page-eyebrow">{eyebrow}</div>}
        <h1 className="page-title">{title}</h1>
        {subtitle && <p className="page-subtitle">{subtitle}</p>}
      </div>
      {actions && <div className="page-actions">{actions}</div>}
    </div>
  );
}

/* ---------- Definition list row ---------- */
export function DLRow({ label, children }: { label: ReactNode; children: ReactNode }) {
  return (
    <div className="dl-row">
      <dt>{label}</dt>
      <dd>{children}</dd>
    </div>
  );
}

/* ---------- Empty state ---------- */
export function EmptyState({ icon, t1, t2 }: { icon?: ReactNode; t1: ReactNode; t2?: ReactNode }) {
  return (
    <div className="empty">
      {icon && <div className="ico">{icon}</div>}
      <div className="t1">{t1}</div>
      {t2 && <div className="t2">{t2}</div>}
    </div>
  );
}

/* ---------- Status flow rail ---------- */
export interface FlowStep {
  value: string;
  label: string;
  sub: string;
}

/**
 * Renders a horizontal status-flow rail. `currentIndex` may be fractional
 * (e.g. 1.5) to indicate an off-main branch; `branch` appends an optional
 * 5th branching node (used for lot 品目変換).
 */
export function StatusFlow({
  steps,
  currentIndex,
  branch,
}: {
  steps: FlowStep[];
  currentIndex: number;
  branch?: { label: string; sub: string; active: boolean; icon?: ReactNode };
}) {
  return (
    <div className="flow">
      {steps.map((s, i) => {
        const state = i < currentIndex ? "completed" : i === currentIndex ? "current" : "pending";
        return (
          <div key={s.value} data-state={state} className="flow-step">
            <div className="flow-node">
              <div className="flow-dot">{i < currentIndex ? <Check size={11} /> : i + 1}</div>
              <div className="l1">{s.label}</div>
            </div>
            <div className="l2 mono">{s.sub}</div>
          </div>
        );
      })}
      {branch && (
        <div className="flow-step" data-state={branch.active ? "current" : "pending"}>
          <div className="flow-node">
            <div className="flow-dot">{branch.active ? branch.icon : steps.length + 1}</div>
            <div className="l1">{branch.label}</div>
          </div>
          <div className="l2 mono">{branch.sub}</div>
        </div>
      )}
    </div>
  );
}
