import { Guard } from "@/components/auth/Guard";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Separator } from "@/components/ui/separator";
import {
  cancelDesignation,
  designateConsignment,
  recordConsignmentResult,
  useConsignmentCase,
} from "@/hooks/use-consignment-case";
import { describeApiError } from "@/lib/api-client";
import { caseStatusLabel } from "@/lib/format";
import { JsonActionForm } from "@/pages/sales-cases/actions/JsonActionForm";
import { Link } from "@tanstack/react-router";
import { useActionState } from "react";
import { toast } from "sonner";

export function ConsignmentCaseDetailPage({ id }: { id: string }) {
  const { data, error, isLoading } = useConsignmentCase(id);

  const version = data?.version;
  const [, cancelAction, isCanceling] = useActionState(async () => {
    if (version == null) {
      toast.error("最新の状態を読み込めませんでした");
      return null;
    }
    if (!confirm("委託指定を解除しますか?")) return null;
    try {
      await cancelDesignation(id, version);
      toast.success("委託指定を解除しました");
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
        <h1 className="font-semibold text-2xl">委託販売案件 {id}</h1>
        <div className="flex items-center gap-2">
          {data.status && (
            <Badge variant="secondary">{caseStatusLabel("consignment", data.status)}</Badge>
          )}
          <Link to="/" className="text-muted-foreground text-sm underline underline-offset-4">
            ホームへ
          </Link>
        </div>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>委託情報</CardTitle>
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
            title="委託指定"
            buttonLabel="登録"
            placeholder='{"consignorName":"...","consignorCode":"..."}'
            onSubmit={(body) => designateConsignment(id, body)}
          />
          <Card>
            <CardHeader>
              <CardTitle className="text-base">委託指定 解除</CardTitle>
            </CardHeader>
            <CardContent>
              <form action={cancelAction}>
                <Button type="submit" variant="destructive" disabled={isCanceling}>
                  {isCanceling ? "解除中…" : "解除"}
                </Button>
              </form>
            </CardContent>
          </Card>
          <JsonActionForm
            title="委託結果 登録"
            buttonLabel="登録"
            onSubmit={(body) => recordConsignmentResult(id, body)}
          />
        </div>
      </Guard>
    </div>
  );
}
