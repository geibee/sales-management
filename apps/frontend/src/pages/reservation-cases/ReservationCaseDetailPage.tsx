import { Guard } from "@/components/auth/Guard";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Separator } from "@/components/ui/separator";
import {
  cancelReservationConfirmation,
  confirmReservation,
  createReservationPrice,
  deliverReservation,
  useReservationCase,
} from "@/hooks/use-reservation-case";
import { describeApiError } from "@/lib/api-client";
import { caseStatusLabel } from "@/lib/format";
import { JsonActionForm } from "@/pages/sales-cases/actions/JsonActionForm";
import { Link } from "@tanstack/react-router";
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
          <JsonActionForm
            title="予約価格 登録"
            buttonLabel="登録"
            onSubmit={(body) => createReservationPrice(id, body)}
          />
          <JsonActionForm
            title="予約 確定"
            buttonLabel="確定"
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
          <JsonActionForm
            title="予約 納品"
            buttonLabel="引き渡し"
            onSubmit={(body) => deliverReservation(id, body)}
          />
        </div>
      </Guard>
    </div>
  );
}
