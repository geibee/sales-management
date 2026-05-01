import { Guard } from "@/components/auth/Guard";
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
  useSalesCase,
} from "@/hooks/use-sales-case";
import { describeApiError } from "@/lib/api-client";
import { caseStatusLabel } from "@/lib/format";
import { Link } from "@tanstack/react-router";
import { useActionState } from "react";
import { toast } from "sonner";
import { JsonActionForm } from "./actions/JsonActionForm";

export function SalesCaseDetailPage({ id }: { id: string }) {
  const { data, error, isLoading } = useSalesCase(id);

  const [, deleteAction, isDeleting] = useActionState(async () => {
    if (!confirm("案件を削除しますか?")) return null;
    try {
      await deleteSalesCase(id);
      toast.success("案件を削除しました");
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
        <h1 className="font-semibold text-2xl">直接販売案件 {id}</h1>
        <div className="flex items-center gap-2">
          <Badge>{data.caseType}</Badge>
          {data.status && (
            <Badge variant="secondary">{caseStatusLabel(data.caseType, data.status)}</Badge>
          )}
          <Link to="/" className="text-muted-foreground text-sm underline underline-offset-4">
            ホームへ
          </Link>
        </div>
      </div>

      <Guard requiredRole="admin">
        <form action={deleteAction}>
          <Button type="submit" variant="destructive" disabled={isDeleting}>
            {isDeleting ? "削除中…" : "案件を削除"}
          </Button>
        </form>
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
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
          <JsonActionForm
            title="価格査定 登録"
            buttonLabel="登録"
            placeholder='{"appraisalDate":"2026-04-28","salesMarket":"国内卸売"}'
            onSubmit={(body) => createAppraisal(id, body)}
          />
          <JsonActionForm
            title="価格査定 更新"
            buttonLabel="更新"
            onSubmit={(body) => updateAppraisal(id, body)}
          />
          <SimpleAction
            title="価格査定 削除"
            onSubmit={() => deleteAppraisal(id, requireVersion(data.version))}
            destructive
          />
          <JsonActionForm
            title="売買契約 登録"
            buttonLabel="登録"
            onSubmit={(body) => createContract(id, body)}
          />
          <SimpleAction
            title="売買契約 削除"
            onSubmit={() => deleteContract(id, requireVersion(data.version))}
            destructive
          />
          <JsonActionForm
            title="出荷指示"
            buttonLabel="登録"
            onSubmit={(body) => instructSalesShipping(id, body)}
          />
          <SimpleAction
            title="出荷指示 解除"
            onSubmit={() => cancelSalesShippingInstruction(id, requireVersion(data.version))}
            destructive
          />
          <JsonActionForm
            title="出荷完了"
            buttonLabel="登録"
            onSubmit={(body) => completeSalesShipping(id, body)}
          />
        </div>
      </Guard>
    </div>
  );
}

function requireVersion(v: number | undefined): number {
  if (v == null) throw new Error("version is missing in case detail response");
  return v;
}

function SimpleAction({
  title,
  onSubmit,
  destructive,
}: {
  title: string;
  onSubmit: () => Promise<void>;
  destructive?: boolean;
}) {
  const [, action, isPending] = useActionState(async () => {
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
        <form action={action}>
          <Button
            type="submit"
            disabled={isPending}
            variant={destructive ? "destructive" : "default"}
          >
            {isPending ? "実行中…" : "実行"}
          </Button>
        </form>
      </CardContent>
    </Card>
  );
}
