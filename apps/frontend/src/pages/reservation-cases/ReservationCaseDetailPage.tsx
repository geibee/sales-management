import { Button } from "@/components/atoms/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/atoms/card";
import {
  CaseStatusPill,
  CaseTypePill,
  DCard,
  DCardBody,
  DCardHeader,
  DLRow,
  type FlowStep,
  Pill,
  StatusFlow,
} from "@/components/design/primitives";
import { Guard } from "@/components/organisms/auth/Guard";
import {
  DateVersionActionForm,
  ReservationConfirmationForm,
  ReservationPriceForm,
} from "@/components/organisms/forms/rich-actions/RichActionForms";
import {
  cancelReservationConfirmation,
  confirmReservation,
  createReservationPrice,
  deliverReservation,
  useReservationCase,
} from "@/hooks/use-reservation-case";
import { describeApiError } from "@/lib/api-client";
import { CalendarClock, Truck } from "lucide-react";
import { useActionState } from "react";
import { toast } from "sonner";

const RESERVATION_FLOW: FlowStep[] = [
  { value: "before_reservation", label: "予約前", sub: "Pre-reservation" },
  { value: "reserved", label: "予約", sub: "Reserved" },
  { value: "reservation_confirmed", label: "予約確定", sub: "Confirmed" },
  { value: "reservation_delivered", label: "引渡", sub: "Delivered" },
];

export function ReservationCaseDetailPage({ id }: { id: string }) {
  const { data, error, isLoading } = useReservationCase(id);

  const version = data?.version;
  const [, cancelAction, isCanceling] = useActionState(async () => {
    if (version == null) {
      toast.error("最新の状態を読み込めませんでした");
      return null;
    }
    if (!confirm("予約確定を取り消しますか?")) return null;
    try {
      await cancelReservationConfirmation(id, version);
      toast.success("予約確定を取り消しました");
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
  if (!data) return null;
  if (data.caseType !== "reservation") {
    return (
      <p className="page" style={{ color: "var(--danger)" }}>
        この画面は予約販売案件用です（このID は {data.caseType} 案件です）。
      </p>
    );
  }

  const flowIdx = RESERVATION_FLOW.findIndex((s) => s.value === data.status);

  return (
    <div className="page">
      <div className="detail-header">
        <div className="detail-header-meta">
          <CaseTypePill caseType="reservation" />
          {data.status && <CaseStatusPill caseType="reservation" status={data.status} />}
          <Pill tone="outline" mono>
            v{data.version}
          </Pill>
        </div>
        <h1>
          予約販売案件 <span className="id">{id}</span>
        </h1>
      </div>

      <StatusFlow steps={RESERVATION_FLOW} currentIndex={flowIdx < 0 ? 0 : flowIdx} />

      <DCard className="mt-6">
        <DCardHeader title="予約情報" icon={<CalendarClock className="ico" size={15} />} />
        <DCardBody>
          <dl className="dl">
            <DLRow label="案件番号">
              <span className="mono">{id}</span>
            </DLRow>
            <DLRow label="販売日">{data.salesDate ?? <span className="subtle">—</span>}</DLRow>
            <DLRow label="事業部">
              <span className="mono">{data.divisionCode ?? "—"}</span>
            </DLRow>
            <DLRow label="対象ロット数">{data.lots.length} 件</DLRow>
            <DLRow label="バージョン">
              <span className="mono">v{data.version}</span>
            </DLRow>
          </dl>
        </DCardBody>
      </DCard>

      <hr className="sep" />

      <Guard
        requiredRole="operator"
        fallback={<p className="muted text-sm">状態遷移には operator 以上のロールが必要です。</p>}
      >
        <div className="grid-2">
          <ReservationPriceForm
            data={data}
            disabled={data.status !== "before_reservation"}
            disabledReason="予約前の案件で実行できます。"
            onSubmit={(body) => createReservationPrice(id, body)}
          />
          <ReservationConfirmationForm
            data={data}
            disabled={data.status !== "reserved"}
            disabledReason="予約済の案件で実行できます。"
            onSubmit={(body) => confirmReservation(id, body)}
          />
          <Card>
            <CardHeader>
              <CardTitle className="text-base">予約確定 取消</CardTitle>
            </CardHeader>
            <CardContent>
              <form action={cancelAction}>
                <Button type="submit" variant="destructive" disabled={isCanceling}>
                  {isCanceling ? "取消中…" : "取消"}
                </Button>
              </form>
            </CardContent>
          </Card>
          <DateVersionActionForm
            title="予約 納品"
            buttonLabel="引き渡し"
            dateLabel="引渡日"
            dateField="deliveryDate"
            defaultDate={data.delivery?.deliveredDate ?? data.salesDate ?? ""}
            version={data.version}
            icon={<Truck className="size-4" />}
            disabled={data.status !== "reservation_confirmed"}
            disabledReason="予約確定済の案件で実行できます。"
            onSubmit={(body) => deliverReservation(id, body)}
          />
        </div>
      </Guard>
    </div>
  );
}

