import { Guard } from "@/components/auth/Guard";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Separator } from "@/components/ui/separator";
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
import {
  codeName,
  formatAmount,
  formatQuantity,
  lotActionEnabled,
  lotStatusLabel,
} from "@/lib/format";
import { Link } from "@tanstack/react-router";
import { useActionState } from "react";
import { toast } from "sonner";
import { LotActionForm } from "./actions/LotActionForm";

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

  if (isLoading) return <p>読み込み中…</p>;
  if (error) return <p className="text-destructive">エラー: {describeApiError(error)}</p>;
  if (!lot) return null;

  const status = lot.status;
  const version = lot.version;

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="font-semibold text-2xl">在庫ロット {lot.lotNumber}</h1>
        <div className="flex items-center gap-2">
          <Guard requiredRole="viewer">
            <form action={exportAction}>
              <Button type="submit" variant="outline" disabled={isExporting}>
                {isExporting ? "エクスポート中…" : "CSV エクスポート"}
              </Button>
            </form>
          </Guard>
          <Link to="/" className="text-muted-foreground text-sm underline underline-offset-4">
            ホームへ
          </Link>
        </div>
      </div>

      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            状態: <Badge variant="secondary">{lotStatusLabel(status)}</Badge>
            <span className="text-muted-foreground text-xs">v{version}</span>
          </CardTitle>
        </CardHeader>
        <CardContent className="space-y-1 text-sm">
          <Field label="ロット番号">{lot.lotNumber}</Field>
          <Field label="事業部">{codeName(lot.division)}</Field>
          <Field label="部">{codeName(lot.department)}</Field>
          <Field label="課">{codeName(lot.section)}</Field>
          <Field label="工程区分">{codeName(lot.processCategory)}</Field>
          <Field label="検査区分">{codeName(lot.inspectionCategory)}</Field>
          <Field label="製造区分">{codeName(lot.manufacturingCategory)}</Field>
          <Field label="製造完了日">{lot.manufacturingCompletedDate ?? "(未設定)"}</Field>
          <Field label="出荷期限日">{lot.shippingDeadlineDate ?? "(未設定)"}</Field>
          <Field label="出荷完了日">{lot.shippedDate ?? "(未設定)"}</Field>
          <Field label="変換先品目">{lot.destinationItem ?? "(未設定)"}</Field>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">明細（{lot.details.length} 件）</CardTitle>
        </CardHeader>
        <CardContent>
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b text-left text-muted-foreground">
                  <th className="py-2 pr-4 font-medium">#</th>
                  <th className="py-2 pr-4 font-medium">品目区分</th>
                  <th className="py-2 pr-4 font-medium">商品分類</th>
                  <th className="py-2 pr-4 font-medium">品質等級</th>
                  <th className="py-2 pr-4 text-right font-medium">数量</th>
                  <th className="py-2 pr-4 text-right font-medium">個数</th>
                  <th className="py-2 pr-4 font-medium">検査結果</th>
                </tr>
              </thead>
              <tbody>
                {lot.details.map((detail, index) => (
                  <tr
                    key={`${detail.productCategoryCode}-${index}`}
                    className="border-b last:border-0"
                  >
                    <td className="py-2 pr-4 text-muted-foreground">{index + 1}</td>
                    <td className="py-2 pr-4">
                      {ITEM_CATEGORY_LABEL[detail.itemCategory] ?? detail.itemCategory}
                    </td>
                    <td className="py-2 pr-4 font-mono">{detail.productCategoryCode}</td>
                    <td className="py-2 pr-4">{detail.qualityGrade}</td>
                    <td className="py-2 pr-4 text-right font-medium tabular-nums">
                      {formatQuantity(detail.quantity)}
                    </td>
                    <td className="py-2 pr-4 text-right tabular-nums">
                      {formatAmount(detail.count)}
                    </td>
                    <td className="py-2 pr-4">
                      {detail.inspectionResultCategory
                        ? (INSPECTION_RESULT_LABEL[detail.inspectionResultCategory] ??
                          detail.inspectionResultCategory)
                        : "(未設定)"}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </CardContent>
      </Card>

      <Separator />

      <Guard
        requiredRole="operator"
        fallback={
          <p className="text-muted-foreground text-sm">
            状態遷移には operator 以上のロールが必要です。
          </p>
        }
      >
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
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

const ITEM_CATEGORY_LABEL: Record<string, string> = {
  general: "通常",
  premium: "上位品",
  custom: "特注",
};

const INSPECTION_RESULT_LABEL: Record<string, string> = {
  pass: "合格",
  fail: "不合格",
};

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="grid grid-cols-[10rem_1fr] gap-2">
      <span className="text-muted-foreground">{label}</span>
      <span>{children}</span>
    </div>
  );
}
