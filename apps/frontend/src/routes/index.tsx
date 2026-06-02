import {
  DCard,
  DCardBody,
  DCardHeader,
  DesignPageHeader,
  EmptyState,
  LotStatusPill,
  Pill,
} from "@/components/design/primitives";
import { useLotsList } from "@/hooks/use-lots-list";
import { useSalesCasesList } from "@/hooks/use-sales-cases-list";
import { LOT_STATUS_LABEL, lotStatusLabel } from "@/lib/format";
import { Link, createFileRoute } from "@tanstack/react-router";
import {
  ArrowRight,
  ClipboardList,
  Globe,
  Layers,
  Package,
  Plus,
  Receipt,
  Sparkles,
  Truck,
} from "lucide-react";
import type { ReactNode } from "react";

export const Route = createFileRoute("/")({
  component: HomePage,
});

const BREAKDOWN_TONE: Record<string, string> = {
  manufacturing: "var(--info)",
  manufactured: "var(--ok)",
  shipping_instructed: "var(--accent-design)",
  shipped: "var(--fg-faint)",
  conversion_instructed: "var(--warn)",
};

function HomePage() {
  const { data: lotsData } = useLotsList({ limit: 100 });
  const { data: casesData } = useSalesCasesList({ limit: 100 });

  const lots = lotsData?.items ?? [];
  const cases = casesData?.items ?? [];
  const lotTotal = lotsData?.total ?? lots.length;
  const caseTotal = casesData?.total ?? cases.length;

  const lotCount = (status: string) => lots.filter((l) => l.status === status).length;
  const manufactured = lotCount("manufactured");
  const shippingInstructed = lotCount("shipping_instructed");
  const beforeAppraisal = cases.filter((c) => c.status === "before_appraisal").length;

  const breakdown = (Object.keys(LOT_STATUS_LABEL) as string[]).map((status) => ({
    status,
    n: lotCount(status),
    color: BREAKDOWN_TONE[status] ?? "var(--fg-faint)",
  }));
  const breakdownTotal = breakdown.reduce((s, b) => s + b.n, 0) || 1;

  return (
    <div className="page">
      <DesignPageHeader
        eyebrow="OVERVIEW"
        title="ダッシュボード"
        subtitle="在庫ロットと販売案件の状況サマリー"
        actions={
          <>
            <Link to="/lots/new" className="btn btn-sm">
              <Plus className="ico" />
              ロット作成
            </Link>
            <Link to="/sales-cases/new" className="btn btn-sm btn-primary">
              <Plus className="ico" />
              販売案件
            </Link>
          </>
        }
      />

      <div className="kpi-grid">
        <div className="kpi">
          <div className="kpi-label">
            <Package className="ico" size={14} />
            在庫ロット総数
          </div>
          <div className="kpi-value tnum">{lotTotal}</div>
        </div>
        <div className="kpi">
          <div className="kpi-label">
            <Truck className="ico" size={14} />
            製造完了・出荷待ち
          </div>
          <div className="kpi-value tnum">{manufactured}</div>
        </div>
        <div className="kpi">
          <div className="kpi-label">
            <ClipboardList className="ico" size={14} />
            査定待ち案件
          </div>
          <div className="kpi-value tnum">{beforeAppraisal}</div>
        </div>
        <div className="kpi">
          <div className="kpi-label">
            <Receipt className="ico" size={14} />
            販売案件総数
          </div>
          <div className="kpi-value tnum">{caseTotal}</div>
        </div>
      </div>

      <div className="dash-grid">
        <DCard>
          <DCardHeader
            title="直近の在庫ロット"
            icon={<Sparkles className="ico" size={15} />}
            actions={
              <Link to="/lots" className="btn btn-sm btn-ghost">
                すべて表示
              </Link>
            }
          />
          <DCardBody tight>
            {lots.length === 0 ? (
              <EmptyState icon={<Package />} t1="ロットがありません" />
            ) : (
              <div className="feed">
                {lots.slice(0, 7).map((l) => (
                  <div key={l.lotNumber} className="feed-row">
                    <div className="marker">
                      <Package className="ico" size={13} />
                    </div>
                    <div className="body">
                      <div className="t">
                        ロット {lotStatusLabel(l.status)} ·{" "}
                        <Link
                          to="/lots/$id"
                          params={{ id: l.lotNumber }}
                          className="lot-ref"
                          style={{ padding: "2px 6px", fontSize: 11.5 }}
                        >
                          {l.lotNumber}
                        </Link>
                      </div>
                      <div className="sub">v{l.version}</div>
                    </div>
                    <div className="when">{l.manufacturingCompletedDate ?? "—"}</div>
                  </div>
                ))}
              </div>
            )}
          </DCardBody>
        </DCard>

        <div className="col gap-4">
          <DCard>
            <DCardHeader title="クイックアクション" icon={<Plus className="ico" size={15} />} />
            <DCardBody tight>
              <div className="col gap-2">
                <QuickAction
                  to="/lots/new"
                  icon={<Package className="ico" />}
                  t1="新しい在庫ロットを登録"
                  t2="製造区分・工程・品目区分を入力"
                />
                <QuickAction
                  to="/sales-cases/new"
                  icon={<Receipt className="ico" />}
                  t1="販売案件を新規作成"
                  t2="直接販売・予約・委託から選択"
                />
                <QuickAction
                  to="/external/price-check"
                  icon={<Globe className="ico" />}
                  t1="外部価格チェック"
                  t2="社外マーケット価格を取得"
                />
              </div>
            </DCardBody>
          </DCard>

          <DCard>
            <DCardHeader
              title="状態別 ロット内訳"
              icon={<Layers className="ico" size={15} />}
              actions={
                <Link to="/lots" className="btn btn-sm btn-ghost">
                  一覧へ
                </Link>
              }
            />
            <DCardBody>
              <div className="col gap-3">
                <div className="bar" role="img" aria-label="状態内訳">
                  {breakdown.map((b) => (
                    <span
                      key={b.status}
                      title={`${lotStatusLabel(b.status)} ${b.n}`}
                      style={{ width: `${(b.n / breakdownTotal) * 100}%`, background: b.color }}
                    />
                  ))}
                </div>
                <div className="col" style={{ gap: 4 }}>
                  {breakdown.map((b) => (
                    <div
                      key={b.status}
                      className="row"
                      style={{ justifyContent: "space-between", fontSize: 12.5 }}
                    >
                      <div className="row gap-2">
                        <span
                          style={{
                            width: 8,
                            height: 8,
                            borderRadius: 2,
                            background: b.color,
                          }}
                        />
                        <span>{lotStatusLabel(b.status)}</span>
                      </div>
                      <span className="mono subtle">{b.n}</span>
                    </div>
                  ))}
                </div>
              </div>
            </DCardBody>
          </DCard>
        </div>
      </div>

      <div className="mt-6">
        <DCard>
          <DCardHeader
            title="最近のロット"
            icon={<Package className="ico" size={15} />}
            actions={
              <>
                <Pill tone="outline" mono>
                  {shippingInstructed} 出荷指示済
                </Pill>
                <Link to="/lots" className="btn btn-sm btn-ghost">
                  すべて表示
                  <ArrowRight className="ico" size={13} />
                </Link>
              </>
            }
          />
          <DCardBody flush>
            <div className="t-scroll">
              <table className="t">
                <thead>
                  <tr>
                    <th>ロット番号</th>
                    <th>状態</th>
                    <th>製造完了</th>
                    <th className="num">version</th>
                  </tr>
                </thead>
                <tbody>
                  {lots.slice(0, 6).map((l) => (
                    <tr key={l.lotNumber}>
                      <td>
                        <Link to="/lots/$id" params={{ id: l.lotNumber }} className="lot-num">
                          {l.lotNumber}
                        </Link>
                      </td>
                      <td>
                        <LotStatusPill status={l.status} />
                      </td>
                      <td className="text-sm">
                        {l.manufacturingCompletedDate ?? <span className="subtle">—</span>}
                      </td>
                      <td className="num subtle text-xs">v{l.version}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </DCardBody>
        </DCard>
      </div>
    </div>
  );
}

function QuickAction({
  to,
  icon,
  t1,
  t2,
}: {
  to: string;
  icon: ReactNode;
  t1: string;
  t2: string;
}) {
  return (
    <Link to={to} className="quick-action">
      <div className="qa-ico">{icon}</div>
      <div className="qa-text">
        <span className="qa-t1">{t1}</span>
        <span className="qa-t2">{t2}</span>
      </div>
      <div className="qa-go">
        <ArrowRight size={15} />
      </div>
    </Link>
  );
}
