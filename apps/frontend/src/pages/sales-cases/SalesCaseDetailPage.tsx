import { Guard } from "@/components/auth/Guard";
import { LotSelectDialog } from "@/components/lots/LotSelectDialog";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Separator } from "@/components/ui/separator";
import {
  cancelSalesShippingInstruction,
  completeSalesShipping,
  createAppraisal,
  createContract,
  deleteAppraisal,
  deleteContract,
  deleteSalesCase,
  instructSalesShipping,
  updateAppraisal,
  updateSalesCaseLots,
  useSalesCase,
} from "@/hooks/use-sales-case";
import { describeApiError } from "@/lib/api-client";
import { caseStatusLabel, formatAmount } from "@/lib/format";
import { Link, useNavigate } from "@tanstack/react-router";
import {
  ArrowLeft,
  CalendarDays,
  CircleDollarSign,
  ClipboardCheck,
  FileText,
  Hash,
  Layers3,
  type LucideIcon,
  Package,
  PackageSearch,
  ReceiptText,
  Trash2,
  Truck,
  Undo2,
} from "lucide-react";
import { type ReactNode, useActionState, useState } from "react";
import { toast } from "sonner";
import {
  DateVersionActionForm,
  DirectAppraisalForm,
  SalesContractForm,
} from "./actions/RichActionForms";

const DIRECT_STATUS_FLOW = [
  "before_appraisal",
  "appraised",
  "contracted",
  "shipping_instructed",
  "shipping_completed",
] as const;

const CASE_TYPE_LABEL: Record<string, string> = {
  direct: "直接販売",
  reservation: "予約",
  consignment: "委託",
};

const FIELD_LABELS: Record<string, string> = {
  type: "査定種別",
  appraisalDate: "査定日",
  deliveryDate: "納期",
  salesMarket: "販売市場",
  taxExcludedEstimatedTotal: "税抜査定合計",
  contractDate: "契約日",
  person: "担当者",
  customerNumber: "顧客番号",
  taxExcludedContractAmount: "税抜契約額",
  consumptionTax: "消費税",
  instructionDate: "指示日",
  completionDate: "完了日",
};

export function SalesCaseDetailPage({ id }: { id: string }) {
  const navigate = useNavigate();
  const { data, error, isLoading } = useSalesCase(id);

  const [, deleteAction, isDeleting] = useActionState(async () => {
    if (!confirm("案件を削除しますか?")) return null;
    try {
      await deleteSalesCase(id);
      toast.success("案件を削除しました");
      navigate({ to: "/sales-cases" });
    } catch (e) {
      toast.error(describeApiError(e));
    }
    return null;
  }, null);

  const [editLotsOpen, setEditLotsOpen] = useState(false);

  if (isLoading) return <p>読み込み中…</p>;
  if (error) return <p className="text-destructive">エラー: {describeApiError(error)}</p>;
  if (!data) return null;

  const statusLabel = caseStatusLabel(data.caseType, data.status);
  // 直接販売は査定登録前のみロット修正可。
  const editLotsAllowed = data.caseType === "direct" && data.status === "before_appraisal";

  const onEditLots = async (lots: string[]) => {
    try {
      await updateSalesCaseLots(id, { lots, version: data.version });
      toast.success("対象ロットを更新しました");
    } catch (e) {
      toast.error(describeApiError(e));
    }
  };
  const lotCount = data.lots.length;

  return (
    <div className="space-y-6" data-testid="sales-case-detail">
      <header className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
        <div className="space-y-3">
          <div className="flex flex-wrap items-center gap-2">
            <Badge variant="outline">{CASE_TYPE_LABEL[data.caseType] ?? data.caseType}</Badge>
            <Badge variant="secondary">{statusLabel}</Badge>
            <Badge variant="outline">v{data.version}</Badge>
          </div>
          <div className="space-y-1">
            <h1 className="font-semibold text-2xl tracking-normal">販売案件 {id}</h1>
            <p className="text-muted-foreground text-sm">
              {data.salesDate} / 事業部 {data.divisionCode} / ロット {lotCount} 件
            </p>
          </div>
        </div>
        <Button asChild variant="outline" size="sm">
          <Link to="/sales-cases">
            <ArrowLeft className="size-4" />
            一覧
          </Link>
        </Button>
      </header>

      <section className="grid grid-cols-1 gap-3 md:grid-cols-4" aria-label="案件サマリー">
        <SummaryTile icon={CalendarDays} label="販売日" value={data.salesDate} />
        <SummaryTile icon={Layers3} label="状態" value={statusLabel} />
        <SummaryTile icon={Package} label="ロット数" value={`${lotCount} 件`} />
        <SummaryTile icon={Hash} label="バージョン" value={`v${data.version}`} />
      </section>

      <section className="space-y-3 rounded-lg border p-4" data-testid="sales-case-status-flow">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h2 className="font-medium text-base">状態フロー</h2>
          <Badge variant="outline">{nextActionLabel(data.status)}</Badge>
        </div>
        <ol className="grid grid-cols-1 gap-2 md:grid-cols-5">
          {DIRECT_STATUS_FLOW.map((status, index) => (
            <StatusStep key={status} status={status} currentStatus={data.status} index={index} />
          ))}
        </ol>
      </section>

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-[0.85fr_1.15fr]">
        <section className="space-y-3 rounded-lg border p-4" data-testid="sales-case-lots">
          <div className="flex items-center justify-between gap-2">
            <div className="flex items-center gap-2">
              <Package className="size-4 text-muted-foreground" />
              <h2 className="font-medium text-base">対象ロット</h2>
            </div>
            {editLotsAllowed && (
              <Guard requiredRole="operator">
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={() => setEditLotsOpen(true)}
                >
                  <PackageSearch className="size-4" />
                  ロットを修正
                </Button>
              </Guard>
            )}
          </div>
          <div className="grid gap-2">
            {data.lots.map((lotNumber) => (
              <Link
                key={lotNumber}
                to="/lots/$id"
                params={{ id: lotNumber }}
                className="flex items-center justify-between rounded-md border px-3 py-2 text-sm transition-colors hover:bg-accent"
              >
                <span className="font-mono">{lotNumber}</span>
                <span className="text-muted-foreground text-xs">詳細</span>
              </Link>
            ))}
          </div>
          {editLotsAllowed && (
            <LotSelectDialog
              open={editLotsOpen}
              onOpenChange={setEditLotsOpen}
              value={data.lots}
              excludeCase={id}
              onConfirm={onEditLots}
              title="対象ロットを修正"
              confirmLabel="更新"
            />
          )}
        </section>

        <section className="space-y-3 rounded-lg border p-4" data-testid="sales-case-business-data">
          <div className="flex items-center gap-2">
            <FileText className="size-4 text-muted-foreground" />
            <h2 className="font-medium text-base">業務データ</h2>
          </div>
          <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
            <DataBlock icon={CircleDollarSign} title="価格査定" data={data.appraisal} />
            <DataBlock icon={ReceiptText} title="売買契約" data={data.contract} />
            <DataBlock icon={ClipboardCheck} title="出荷指示" data={data.shippingInstruction} />
            <DataBlock icon={Truck} title="出荷完了" data={data.shippingCompletion} />
          </div>
        </section>
      </div>

      <Guard requiredRole="admin">
        <section className="rounded-lg border border-destructive/30 p-4">
          <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
            <div>
              <h2 className="font-medium text-base">管理操作</h2>
              <p className="text-muted-foreground text-sm">現在の状態: {statusLabel}</p>
            </div>
            <form action={deleteAction}>
              <Button
                type="submit"
                variant="destructive"
                disabled={isDeleting || data.status !== "before_appraisal"}
              >
                <Trash2 className="size-4" />
                {isDeleting ? "削除中…" : "案件を削除"}
              </Button>
            </form>
          </div>
        </section>
      </Guard>

      <Separator />

      <Guard
        requiredRole="operator"
        fallback={
          <p className="text-muted-foreground text-sm">
            状態遷移には operator 以上のロールが必要です。
          </p>
        }
      >
        <section className="space-y-3" data-testid="sales-case-actions">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <h2 className="font-medium text-base">状態遷移</h2>
            <Badge variant="secondary">{nextActionLabel(data.status)}</Badge>
          </div>
          <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
            <DirectAppraisalForm
              data={data}
              title="価格査定 登録"
              buttonLabel="登録"
              disabled={data.status !== "before_appraisal"}
              disabledReason="査定前の案件で実行できます。"
              onSubmit={(body) => createAppraisal(id, body)}
            />
            <DirectAppraisalForm
              data={data}
              title="価格査定 更新"
              buttonLabel="更新"
              disabled={data.status !== "appraised"}
              disabledReason="査定済の案件で実行できます。"
              onSubmit={(body) => updateAppraisal(id, body)}
            />
            <SimpleAction
              title="価格査定 削除"
              icon={<Trash2 className="size-4" />}
              onSubmit={() => deleteAppraisal(id, requireVersion(data.version))}
              disabled={data.status !== "appraised"}
              disabledReason="査定済の案件で実行できます。"
              destructive
            />
            <SalesContractForm
              data={data}
              title="売買契約 登録"
              buttonLabel="登録"
              disabled={data.status !== "appraised"}
              disabledReason="査定済の案件で実行できます。"
              onSubmit={(body) => createContract(id, body)}
            />
            <SimpleAction
              title="売買契約 削除"
              icon={<Trash2 className="size-4" />}
              onSubmit={() => deleteContract(id, requireVersion(data.version))}
              disabled={data.status !== "contracted"}
              disabledReason="契約済の案件で実行できます。"
              destructive
            />
            <DateVersionActionForm
              title="出荷指示"
              buttonLabel="登録"
              dateLabel="指示日"
              defaultDate={recordString(
                data.shippingInstruction,
                "instructionDate",
                data.salesDate,
              )}
              version={data.version}
              icon={<ClipboardCheck className="size-4" />}
              disabled={data.status !== "contracted"}
              disabledReason="契約済の案件で実行できます。"
              onSubmit={(body) => instructSalesShipping(id, body)}
            />
            <SimpleAction
              title="出荷指示 解除"
              icon={<Undo2 className="size-4" />}
              onSubmit={() => cancelSalesShippingInstruction(id, requireVersion(data.version))}
              disabled={data.status !== "shipping_instructed"}
              disabledReason="出荷指示済の案件で実行できます。"
              destructive
            />
            <DateVersionActionForm
              title="出荷完了"
              buttonLabel="登録"
              dateLabel="完了日"
              defaultDate={recordString(data.shippingCompletion, "completionDate", data.salesDate)}
              version={data.version}
              icon={<Truck className="size-4" />}
              disabled={data.status !== "shipping_instructed"}
              disabledReason="出荷指示済の案件で実行できます。"
              onSubmit={(body) => completeSalesShipping(id, body)}
            />
          </div>
        </section>
      </Guard>
    </div>
  );
}

function requireVersion(v: number | undefined): number {
  if (v == null) throw new Error("version is missing in case detail response");
  return v;
}

function SummaryTile({
  icon: Icon,
  label,
  value,
}: {
  icon: LucideIcon;
  label: string;
  value: string;
}) {
  return (
    <div className="rounded-lg border p-4">
      <div className="flex items-center gap-2 text-muted-foreground text-xs">
        <Icon className="size-4" />
        <span>{label}</span>
      </div>
      <p className="mt-2 truncate font-medium text-sm">{value}</p>
    </div>
  );
}

function StatusStep({
  status,
  currentStatus,
  index,
}: {
  status: (typeof DIRECT_STATUS_FLOW)[number];
  currentStatus: string;
  index: number;
}) {
  const currentIndex = DIRECT_STATUS_FLOW.findIndex((item) => item === currentStatus);
  const state = index < currentIndex ? "completed" : index === currentIndex ? "current" : "pending";

  return (
    <li
      data-state={state}
      className="rounded-lg border p-3 data-[state=completed]:border-primary/40 data-[state=current]:border-primary data-[state=current]:bg-accent"
    >
      <div className="flex items-center gap-2">
        <span className="flex size-6 items-center justify-center rounded-full border font-mono text-xs">
          {index + 1}
        </span>
        <span className="font-medium text-sm">{caseStatusLabel("direct", status)}</span>
      </div>
    </li>
  );
}

function DataBlock({
  icon: Icon,
  title,
  data,
}: {
  icon: LucideIcon;
  title: string;
  data: unknown;
}) {
  const entries = recordEntries(data);
  const hasData = entries.length > 0;

  return (
    <div className="rounded-lg border p-4" data-state={hasData ? "filled" : "empty"}>
      <div className="flex items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <Icon className="size-4 text-muted-foreground" />
          <h3 className="font-medium text-sm">{title}</h3>
        </div>
        <Badge variant={hasData ? "secondary" : "outline"}>{hasData ? "登録済" : "未登録"}</Badge>
      </div>
      {hasData ? (
        <dl className="mt-3 grid gap-2">
          {entries.map(([key, value]) => (
            <div key={key} className="grid grid-cols-[8rem_1fr] gap-2 text-sm">
              <dt className="text-muted-foreground">{FIELD_LABELS[key] ?? key}</dt>
              <dd className="min-w-0 break-words">{formatFieldValue(value)}</dd>
            </div>
          ))}
        </dl>
      ) : (
        <p className="mt-3 text-muted-foreground text-sm">未登録</p>
      )}
    </div>
  );
}

function SimpleAction({
  title,
  icon,
  onSubmit,
  destructive,
  disabled,
  disabledReason,
}: {
  title: string;
  icon?: ReactNode;
  onSubmit: () => Promise<void>;
  destructive?: boolean;
  disabled?: boolean;
  disabledReason?: string;
}) {
  const [, action, isPending] = useActionState(async () => {
    if (disabled) return null;
    if (!confirm(`${title} を実行しますか?`)) return null;
    try {
      await onSubmit();
      toast.success(`${title} を実行しました`);
    } catch (e) {
      toast.error(describeApiError(e));
    }
    return null;
  }, null);
  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">{title}</CardTitle>
      </CardHeader>
      <CardContent>
        <form action={action} className="space-y-3">
          {disabled && disabledReason && (
            <p className="text-muted-foreground text-xs">{disabledReason}</p>
          )}
          <Button
            type="submit"
            disabled={isPending || disabled}
            variant={destructive ? "destructive" : "default"}
          >
            {icon}
            {isPending ? "実行中…" : "実行"}
          </Button>
        </form>
      </CardContent>
    </Card>
  );
}

function nextActionLabel(status: string): string {
  switch (status) {
    case "before_appraisal":
      return "次: 価格査定";
    case "appraised":
      return "次: 売買契約";
    case "contracted":
      return "次: 出荷指示";
    case "shipping_instructed":
      return "次: 出荷完了";
    case "shipping_completed":
      return "完了";
    default:
      return "状態確認";
  }
}

function recordEntries(value: unknown): Array<[string, unknown]> {
  if (!isRecord(value)) return [];
  return Object.entries(value).filter(
    ([, item]) => item !== null && item !== undefined && item !== "",
  );
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function recordString(source: unknown, key: string, fallback: string): string {
  if (!isRecord(source)) return fallback;
  const value = source[key];
  return typeof value === "string" && value.trim() ? value : fallback;
}

function formatFieldValue(value: unknown): string {
  if (typeof value === "number") return formatAmount(value);
  if (typeof value === "string") return value;
  if (typeof value === "boolean") return value ? "true" : "false";
  if (Array.isArray(value)) return value.map(formatFieldValue).join(", ");
  if (isRecord(value)) return JSON.stringify(value);
  return String(value);
}
