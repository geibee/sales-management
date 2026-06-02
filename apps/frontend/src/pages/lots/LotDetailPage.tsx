import {
  DCard,
  DCardBody,
  DCardHeader,
  DLRow,
  type FlowStep,
  LotStatusPill,
  Pill,
  StatusFlow,
} from "@/components/design/primitives";
import { Guard } from "@/components/organisms/auth/Guard";
import { LotActionForm } from "@/components/organisms/forms/LotActionForm";
import {
  cancelItemConversionInstruction,
  cancelManufacturingCompletion,
  completeManufacturing,
  completeShipping,
  exportLotsCsv,
  instructItemConversion,
  instructShipping,
  useLot,
} from "@/hooks/use-lot";
import { describeApiError } from "@/lib/api-client";
import { codeName, formatAmount, formatQuantity, lotActionEnabled } from "@/lib/format";
import { Calendar, Download, Layers, RefreshCw, Tag } from "lucide-react";
import { useActionState } from "react";
import { toast } from "sonner";

const LOT_FLOW: FlowStep[] = [
  { value: "manufacturing", label: "製造中", sub: "Manufacturing" },
  { value: "manufactured", label: "製造完了", sub: "Completed" },
  { value: "shipping_instructed", label: "出荷指示済", sub: "Shipping ordered" },
  { value: "shipped", label: "出荷完了", sub: "Shipped" },
];

function flowIndexFor(status: string): number {
  if (status === "conversion_instructed") return 1.5;
  const idx = LOT_FLOW.findIndex((s) => s.value === status);
  return idx < 0 ? 0 : idx;
}

const ITEM_CATEGORY_LABEL: Record<string, string> = {
  general: "通常",
  premium: "上位品",
  custom: "特注",
};

export function LotDetailPage({ id }: { id: string }) {
  const { data: lot, error, isLoading } = useLot(id);

  const [, exportAction, isExporting] = useActionState(async () => {
    try {
      await exportLotsCsv();
      toast.success("CSV をダウンロードしました");
    } catch (e) {
      toast.error(describeApiError(e));
    }
    return null;
  }, null);

  if (isLoading) return <p className="page">読み込み中…</p>;
  if (error)
    return (
      <p className="page" style={{ color: "var(--danger)" }}>
        エラー: {describeApiError(error)}
      </p>
    );
  if (!lot) return null;

  const status = lot.status;
  const version = lot.version;

  return (
    <div className="page">
      <div className="detail-header">
        <div className="detail-header-meta">
          <Pill tone="outline">在庫ロット</Pill>
          <LotStatusPill status={status} />
          <Pill tone="outline" mono>
            v{version}
          </Pill>
        </div>
        <div className="row" style={{ justifyContent: "space-between", alignItems: "flex-start" }}>
          <div>
            <h1>
              在庫ロット <span className="id">{lot.lotNumber}</span>
            </h1>
            <div className="muted text-sm mt-2">
              製造完了 {lot.manufacturingCompletedDate ?? "—"} · 出荷期限{" "}
              {lot.shippingDeadlineDate ?? "—"} · 明細 {lot.details.length} 件
            </div>
          </div>
          <div className="detail-actions">
            <Guard requiredRole="viewer">
              <form action={exportAction}>
                <button type="submit" className="btn btn-sm btn-ghost" disabled={isExporting}>
                  <Download className="ico" />
                  {isExporting ? "エクスポート中…" : "CSV エクスポート"}
                </button>
              </form>
            </Guard>
          </div>
        </div>
      </div>

      <StatusFlow
        steps={LOT_FLOW}
        currentIndex={flowIndexFor(status)}
        branch={{
          label: "品目変換",
          sub: "Item conversion (任意分岐)",
          active: status === "conversion_instructed",
          icon: <RefreshCw size={11} />,
        }}
      />

      <div className="split-2 mt-6">
        <DCard>
          <DCardHeader title="基本情報" icon={<Tag className="ico" size={15} />} />
          <DCardBody>
            <dl className="dl">
              <DLRow label="ロット番号">
                <span className="mono">{lot.lotNumber}</span>
              </DLRow>
              <DLRow label="事業部">{codeName(lot.division)}</DLRow>
              <DLRow label="部">{codeName(lot.department)}</DLRow>
              <DLRow label="課">{codeName(lot.section)}</DLRow>
              <DLRow label="工程区分">{codeName(lot.processCategory)}</DLRow>
              <DLRow label="検査区分">{codeName(lot.inspectionCategory)}</DLRow>
              <DLRow label="製造区分">{codeName(lot.manufacturingCategory)}</DLRow>
            </dl>
          </DCardBody>
        </DCard>
        <DCard>
          <DCardHeader title="日付・状態" icon={<Calendar className="ico" size={15} />} />
          <DCardBody>
            <dl className="dl">
              <DLRow label="製造完了日">
                {lot.manufacturingCompletedDate ?? <span className="subtle">(未設定)</span>}
              </DLRow>
              <DLRow label="出荷期限日">
                {lot.shippingDeadlineDate ?? <span className="subtle">(未設定)</span>}
              </DLRow>
              <DLRow label="出荷完了日">
                {lot.shippedDate ?? <span className="subtle">(未設定)</span>}
              </DLRow>
              <DLRow label="変換先品目">
                {lot.destinationItem ?? <span className="subtle">(未設定)</span>}
              </DLRow>
              <DLRow label="現在の状態">
                <LotStatusPill status={status} />
              </DLRow>
              <DLRow label="バージョン">
                <span className="mono">v{version}</span>
              </DLRow>
            </dl>
          </DCardBody>
        </DCard>
      </div>

      <DCard className="mt-6">
        <DCardHeader
          title={`明細 (${lot.details.length} 件)`}
          icon={<Layers className="ico" size={15} />}
        />
        <DCardBody flush>
          <div className="t-scroll">
            <table className="t">
              <thead>
                <tr>
                  <th style={{ width: 40 }}>#</th>
                  <th>品目区分</th>
                  <th>商品分類</th>
                  <th>品質等級</th>
                  <th className="num">数量</th>
                  <th className="num">個数</th>
                  <th>検査結果</th>
                </tr>
              </thead>
              <tbody>
                {lot.details.map((detail, index) => (
                  <tr key={`${detail.productCategoryCode}-${index}`}>
                    <td className="muted mono">{index + 1}</td>
                    <td>{ITEM_CATEGORY_LABEL[detail.itemCategory] ?? detail.itemCategory}</td>
                    <td>
                      <span className="mono">{detail.productCategoryCode}</span>
                    </td>
                    <td>
                      <Pill tone="outline">{detail.qualityGrade}</Pill>
                    </td>
                    <td className="num">{formatQuantity(detail.quantity)}</td>
                    <td className="num">{formatAmount(detail.count)}</td>
                    <td>
                      {detail.inspectionResultCategory === "pass" ? (
                        <Pill tone="ok" dot>
                          合格
                        </Pill>
                      ) : detail.inspectionResultCategory === "fail" ? (
                        <Pill tone="danger" dot>
                          不合格
                        </Pill>
                      ) : (
                        <span className="subtle text-sm">(未設定)</span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </DCardBody>
      </DCard>

      <hr className="sep" />

      <Guard
        requiredRole="operator"
        fallback={<p className="muted text-sm">状態遷移には operator 以上のロールが必要です。</p>}
      >
        <div className="grid-2">
          <LotActionForm
            title="製造完了"
            withDate
            buttonLabel="製造完了を登録"
            disabled={!lotActionEnabled("complete-manufacturing", status)}
            onSubmit={async (date) => {
              await completeManufacturing(id, date ?? "", version);
            }}
          />
          <LotActionForm
            title="製造完了を取消"
            buttonLabel="取消を実行"
            destructive
            disabled={!lotActionEnabled("cancel-manufacturing-completion", status)}
            onSubmit={async () => {
              await cancelManufacturingCompletion(id, version);
            }}
          />
          <LotActionForm
            title="出荷指示"
            withDate
            dateLabel="出荷期限"
            buttonLabel="出荷指示を登録"
            disabled={!lotActionEnabled("instruct-shipping", status)}
            onSubmit={async (date) => {
              await instructShipping(id, date ?? "", version);
            }}
          />
          <LotActionForm
            title="出荷完了"
            withDate
            buttonLabel="出荷完了を登録"
            disabled={!lotActionEnabled("complete-shipping", status)}
            onSubmit={async (date) => {
              await completeShipping(id, date ?? "", version);
            }}
          />
          <LotActionForm
            title="品目変換を指示"
            withText
            textLabel="変換先品目"
            textPlaceholder="例: 2025-T-902"
            buttonLabel="品目変換を指示"
            disabled={!lotActionEnabled("instruct-item-conversion", status)}
            onSubmit={async (_date, text) => {
              await instructItemConversion(id, text ?? "", version);
            }}
          />
          <LotActionForm
            title="品目変換指示を取消"
            buttonLabel="取消を実行"
            destructive
            disabled={!lotActionEnabled("cancel-item-conversion-instruction", status)}
            onSubmit={async () => {
              await cancelItemConversionInstruction(id, version);
            }}
          />
        </div>
      </Guard>
    </div>
  );
}
