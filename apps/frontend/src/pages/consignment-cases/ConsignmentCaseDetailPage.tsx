import { Badge } from "@/components/atoms/badge";
import { Button } from "@/components/atoms/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/atoms/card";
import { Separator } from "@/components/atoms/separator";
import { Guard } from "@/components/organisms/auth/Guard";
import { LotSelectDialog } from "@/components/organisms/dialogs/LotSelectDialog";
import {
  cancelDesignation,
  designateConsignment,
  recordConsignmentResult,
  useConsignmentCase,
} from "@/hooks/use-consignment-case";
import { updateSalesCaseLots } from "@/hooks/use-sales-case";
import { describeApiError } from "@/lib/api-client";
import { caseStatusLabel } from "@/lib/format";
import {
  ConsignmentDesignationForm,
  ConsignmentResultForm,
} from "@/pages/sales-cases/actions/RichActionForms";
import { Link } from "@tanstack/react-router";
import { PackageSearch } from "lucide-react";
import { useActionState, useState } from "react";
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

  const [editLotsOpen, setEditLotsOpen] = useState(false);

  if (isLoading) return <p>読み込み中…</p>;
  if (error) return <p className="text-destructive">エラー: {describeApiError(error)}</p>;
  if (!data) return null;

  // 委託は委託指定前のみロット修正可。
  const editLotsAllowed = data.status === "before_consignment";

  const onEditLots = async (lots: string[]) => {
    if (data.version == null) {
      toast.error("最新の状態を読み込めませんでした");
      return;
    }
    try {
      await updateSalesCaseLots(id, { lots, version: data.version });
      toast.success("対象ロットを更新しました");
    } catch (e) {
      toast.error(describeApiError(e));
    }
  };

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
          <div className="flex items-center justify-between gap-2">
            <CardTitle className="text-base">対象ロット</CardTitle>
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
        </CardHeader>
        <CardContent className="space-y-1">
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
        </CardContent>
      </Card>

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
          <ConsignmentDesignationForm
            data={data}
            disabled={data.status !== "before_consignment"}
            disabledReason="委託前の案件で実行できます。"
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
          <ConsignmentResultForm
            data={data}
            disabled={data.status !== "consignment_designated"}
            disabledReason="委託指定済の案件で実行できます。"
            onSubmit={(body) => recordConsignmentResult(id, body)}
          />
        </div>
      </Guard>
    </div>
  );
}
