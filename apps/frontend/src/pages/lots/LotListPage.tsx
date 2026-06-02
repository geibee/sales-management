import { DesignPageHeader, EmptyState, LotStatusPill } from "@/components/design/primitives";
import { SalesCaseCreateDialog } from "@/components/organisms/dialogs/SalesCaseCreateDialog";
import { useLotsList } from "@/hooks/use-lots-list";
import { describeApiError } from "@/lib/api-client";
import { Link } from "@tanstack/react-router";
import {
  BriefcaseBusiness,
  ChevronLeft,
  ChevronRight,
  Download,
  Package,
  Plus,
  X,
} from "lucide-react";
import { useState } from "react";

const PAGE_SIZE = 20;
const STATUS_CHIPS = [
  { v: "all", label: "すべて" },
  { v: "manufacturing", label: "製造中" },
  { v: "manufactured", label: "製造完了" },
  { v: "shipping_instructed", label: "出荷指示済" },
  { v: "shipped", label: "出荷完了" },
  { v: "conversion_instructed", label: "品目変換指示済" },
] as const;

export function LotListPage() {
  const [status, setStatus] = useState<string>("all");
  const [offset, setOffset] = useState(0);
  const { data, error, isLoading } = useLotsList({ status, limit: PAGE_SIZE, offset });

  const total = data?.total ?? 0;
  const items = data?.items ?? [];
  const page = Math.floor(offset / PAGE_SIZE) + 1;
  const lastPage = Math.max(1, Math.ceil(total / PAGE_SIZE));

  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [createOpen, setCreateOpen] = useState(false);

  const toggle = (lotNumber: string) => {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(lotNumber)) next.delete(lotNumber);
      else next.add(lotNumber);
      return next;
    });
  };

  return (
    <div className="page">
      <DesignPageHeader
        eyebrow="在庫管理"
        title="在庫ロット"
        subtitle={`${total} 件のロット`}
        actions={
          <Link to="/lots/new" className="btn btn-sm btn-primary">
            <Plus className="ico" />
            新規ロット
          </Link>
        }
      />

      <SalesCaseCreateDialog
        open={createOpen}
        onOpenChange={setCreateOpen}
        lotNumbers={[...selected]}
      />

      <div className="t-wrap mb-3">
        <div className="t-toolbar" style={{ flexWrap: "wrap" }}>
          <div className="chips">
            {STATUS_CHIPS.map((c) => (
              <button
                type="button"
                key={c.v}
                className={`chip ${status === c.v ? "on" : ""}`}
                onClick={() => {
                  setStatus(c.v);
                  setOffset(0);
                }}
              >
                {c.label}
              </button>
            ))}
          </div>
          <div style={{ flex: 1 }} />
          <span className="text-sm muted">
            <span className="mono tnum">{total}</span> 件
          </span>
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
                <th style={{ width: 36 }} />
                <th>ロット番号</th>
                <th>状態</th>
                <th>製造完了日</th>
                <th className="num">version</th>
                <th style={{ width: 36 }} />
              </tr>
            </thead>
            <tbody>
              {isLoading && items.length === 0 ? (
                <tr>
                  <td colSpan={6} className="muted" style={{ padding: "16px var(--cell-px)" }}>
                    読み込み中…
                  </td>
                </tr>
              ) : items.length === 0 ? (
                <tr>
                  <td colSpan={6}>
                    <EmptyState
                      icon={<Package />}
                      t1="該当するロットがありません"
                      t2="フィルタを変更してください"
                    />
                  </td>
                </tr>
              ) : (
                items.map((it) => {
                  const sel = selected.has(it.lotNumber);
                  const disabled = it.status !== "manufactured";
                  return (
                    <tr key={it.lotNumber} className={sel ? "selected" : ""}>
                      <td>
                        <input
                          type="checkbox"
                          className="size-4 disabled:opacity-40"
                          checked={sel}
                          disabled={disabled}
                          onChange={() => toggle(it.lotNumber)}
                          aria-label={`ロット ${it.lotNumber} を選択`}
                          title={disabled ? "製造完了ロットのみ選択できます" : undefined}
                        />
                      </td>
                      <td>
                        <Link to="/lots/$id" params={{ id: it.lotNumber }} className="lot-num">
                          {it.lotNumber}
                        </Link>
                      </td>
                      <td>
                        <LotStatusPill status={it.status} />
                      </td>
                      <td className="text-sm">
                        {it.manufacturingCompletedDate ?? <span className="subtle">—</span>}
                      </td>
                      <td className="num subtle text-xs">v{it.version}</td>
                      <td>
                        <Link
                          to="/lots/$id"
                          params={{ id: it.lotNumber }}
                          className="icon-btn"
                          aria-label={`ロット ${it.lotNumber} の詳細`}
                        >
                          <ChevronRight size={14} />
                        </Link>
                      </td>
                    </tr>
                  );
                })
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

      {selected.size > 0 && (
        <div
          style={{
            position: "fixed",
            bottom: 32,
            left: "50%",
            transform: "translateX(-50%)",
            zIndex: 50,
            marginLeft: "calc(var(--rail-w) / 2)",
          }}
        >
          <div className="bulkbar">
            <span className="count">{selected.size}</span>
            <span>件のロットを選択中</span>
            <div style={{ width: 1, height: 18, background: "oklch(0.4 0.01 80)" }} />
            <button
              type="button"
              className="btn btn-sm btn-primary"
              onClick={() => setCreateOpen(true)}
            >
              <BriefcaseBusiness size={13} />
              販売案件新規登録（{selected.size}）
            </button>
            <button type="button" className="btn btn-sm">
              <Download size={13} />
              エクスポート
            </button>
            <button
              type="button"
              className="icon-btn"
              title="選択解除"
              aria-label="選択解除"
              onClick={() => setSelected(new Set())}
              style={{ color: "var(--bg-elev)" }}
            >
              <X size={14} />
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
