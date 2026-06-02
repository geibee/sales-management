import {
  CaseStatusPill,
  CaseTypePill,
  DesignPageHeader,
  EmptyState,
} from "@/components/design/primitives";
import { useSalesCasesList } from "@/hooks/use-sales-cases-list";
import { describeApiError } from "@/lib/api-client";
import { caseStatusLabel } from "@/lib/format";
import { Link } from "@tanstack/react-router";
import { ChevronLeft, ChevronRight, Plus, Receipt } from "lucide-react";
import { useState } from "react";

const PAGE_SIZE = 20;
const TYPE_CHIPS = [
  { v: "all", label: "すべて" },
  { v: "direct", label: "直接販売" },
  { v: "reservation", label: "予約" },
  { v: "consignment", label: "委託" },
] as const;

const STATUS_OPTIONS = [
  "all",
  "before_appraisal",
  "appraised",
  "contracted",
  "shipping_instructed",
  "shipping_completed",
  "before_reservation",
  "reserved",
  "reservation_confirmed",
  "reservation_delivered",
  "before_consignment",
  "consignment_designated",
  "consignment_result_entered",
] as const;

function detailRouteFor(
  caseType: string,
): "/sales-cases/$id" | "/reservation-cases/$id" | "/consignment-cases/$id" {
  if (caseType === "reservation") return "/reservation-cases/$id";
  if (caseType === "consignment") return "/consignment-cases/$id";
  return "/sales-cases/$id";
}

export function SalesCaseListPage() {
  const [status, setStatus] = useState<string>("all");
  const [caseType, setCaseType] = useState<string>("all");
  const [offset, setOffset] = useState(0);
  const { data, error, isLoading } = useSalesCasesList({
    status,
    caseType,
    limit: PAGE_SIZE,
    offset,
  });

  const total = data?.total ?? 0;
  const items = data?.items ?? [];
  const page = Math.floor(offset / PAGE_SIZE) + 1;
  const lastPage = Math.max(1, Math.ceil(total / PAGE_SIZE));

  return (
    <div className="page">
      <DesignPageHeader
        eyebrow="販売管理"
        title="販売案件"
        subtitle={`${total} 件の案件`}
        actions={
          <Link to="/sales-cases/new" className="btn btn-sm btn-primary">
            <Plus className="ico" />
            新規案件
          </Link>
        }
      />

      <div className="t-wrap">
        <div className="t-toolbar" style={{ flexWrap: "wrap" }}>
          <div className="chips">
            {TYPE_CHIPS.map((c) => (
              <button
                type="button"
                key={c.v}
                className={`chip ${caseType === c.v ? "on" : ""}`}
                onClick={() => {
                  setCaseType(c.v);
                  setOffset(0);
                }}
              >
                {c.label}
              </button>
            ))}
          </div>
          <div style={{ flex: 1 }} />
          <select
            className="select"
            style={{ width: 200, height: 30 }}
            value={status}
            aria-label="状態フィルタ"
            onChange={(e) => {
              setStatus(e.target.value);
              setOffset(0);
            }}
          >
            {STATUS_OPTIONS.map((s) => (
              <option key={s} value={s}>
                {s === "all"
                  ? "(すべての状態)"
                  : caseStatusLabel(caseType === "all" ? null : caseType, s)}
              </option>
            ))}
          </select>
        </div>

        {error && (
          <div className="t-toolbar" style={{ borderTop: 0 }}>
            <p className="text-sm" style={{ color: "var(--danger)" }}>
              エラー: {describeApiError(error)}
            </p>
          </div>
        )}

        <div className="t-scroll">
          <table className="t">
            <thead>
              <tr>
                <th>案件番号</th>
                <th>種別</th>
                <th>状態</th>
                <th>販売日</th>
                <th style={{ width: 36 }} />
              </tr>
            </thead>
            <tbody>
              {isLoading && items.length === 0 ? (
                <tr>
                  <td colSpan={5} className="muted" style={{ padding: "16px var(--cell-px)" }}>
                    読み込み中…
                  </td>
                </tr>
              ) : items.length === 0 ? (
                <tr>
                  <td colSpan={5}>
                    <EmptyState
                      icon={<Receipt />}
                      t1="該当する案件がありません"
                      t2="種別・状態フィルタを変更してください"
                    />
                  </td>
                </tr>
              ) : (
                items.map((it) => (
                  <tr key={it.salesCaseNumber}>
                    <td>
                      <Link
                        to={detailRouteFor(it.caseType)}
                        params={{ id: it.salesCaseNumber }}
                        className="lot-num"
                      >
                        {it.salesCaseNumber}
                      </Link>
                    </td>
                    <td>
                      <CaseTypePill caseType={it.caseType} />
                    </td>
                    <td>
                      <CaseStatusPill caseType={it.caseType} status={it.status} />
                    </td>
                    <td className="text-sm">{it.salesDate ?? <span className="subtle">—</span>}</td>
                    <td>
                      <Link
                        to={detailRouteFor(it.caseType)}
                        params={{ id: it.salesCaseNumber }}
                        className="icon-btn"
                        aria-label={`案件 ${it.salesCaseNumber} の詳細`}
                      >
                        <ChevronRight size={14} />
                      </Link>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>

        <div
          className="t-toolbar"
          style={{
            borderTop: "1px solid var(--border-design)",
            borderBottom: 0,
            justifyContent: "space-between",
          }}
        >
          <div className="text-sm muted">
            <span className="mono tnum">{items.length}</span> /{" "}
            <span className="mono tnum">{total}</span> 件
          </div>
          <div className="row gap-2">
            <button
              type="button"
              className="btn btn-sm btn-ghost"
              disabled={offset === 0}
              onClick={() => setOffset(Math.max(0, offset - PAGE_SIZE))}
            >
              <ChevronLeft size={13} />
              前へ
            </button>
            <span className="text-sm muted mono">
              {page} / {lastPage}
            </span>
            <button
              type="button"
              className="btn btn-sm btn-ghost"
              disabled={offset + PAGE_SIZE >= total}
              onClick={() => setOffset(offset + PAGE_SIZE)}
            >
              次へ
              <ChevronRight size={13} />
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
