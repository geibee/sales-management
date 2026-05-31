import { Badge } from "@/components/atoms/badge";
import { Button } from "@/components/atoms/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/atoms/card";
import { Separator } from "@/components/atoms/separator";
import { Guard } from "@/components/organisms/auth/Guard";
import {
  cancelReservationConfirmation,
  confirmReservation,
  createReservationPrice,
  deliverReservation,
  useReservationCase,
} from "@/hooks/use-reservation-case";
import { describeApiError } from "@/lib/api-client";
import { caseStatusLabel } from "@/lib/format";
import {
  DateVersionActionForm,
  ReservationConfirmationForm,
  ReservationPriceForm,
} from "@/pages/sales-cases/actions/RichActionForms";
import { Link } from "@tanstack/react-router";
import { Truck } from "lucide-react";
import { useActionState } from "react";
import { toast } from "sonner";

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

  if (isLoading) return <p>読み込み中…</p>;
  if (error) return <p className="text-destructive">エラー: {describeApiError(error)}</p>;
  if (!data) return null;

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="font-semibold text-2xl">予約販売案件 {id}</h1>
        <div className="flex items-center gap-2">
          {data.status && (
            <Badge variant="secondary">{caseStatusLabel("reservation", data.status)}</Badge>
          )}
          <Link to="/" className="text-muted-foreground text-sm underline underline-offset-4">
            ホームへ
          </Link>
        </div>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>予約情報</CardTitle>
        </CardHeader>
        <CardContent>
          <pre className="overflow-x-auto text-xs">{JSON.stringify(data, null, 2)}</pre>
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
            defaultDate={recordString(data.delivery, "deliveredDate", data.salesDate)}
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

function recordString(source: unknown, key: string, fallback: string): string {
  if (typeof source !== "object" || source === null || Array.isArray(source)) return fallback;
  const value = (source as Record<string, unknown>)[key];
  return typeof value === "string" && value.trim() ? value : fallback;
}
