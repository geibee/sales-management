import { Button } from "@/components/atoms/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/atoms/card";
import {
  CaseStatusPill,
  CaseTypePill,
  DCard,
  DCardBody,
  DCardHeader,
  type FlowStep,
  Pill,
  StatusFlow,
} from "@/components/design/primitives";
import { Guard } from "@/components/organisms/auth/Guard";
import { LotSelectDialog } from "@/components/organisms/dialogs/LotSelectDialog";
import {
  ConsignmentDesignationForm,
  ConsignmentResultForm,
} from "@/components/organisms/forms/rich-actions/RichActionForms";
import {
  cancelDesignation,
  designateConsignment,
  recordConsignmentResult,
  useConsignmentCase,
} from "@/hooks/use-consignment-case";
import { updateSalesCaseLots } from "@/hooks/use-sales-case";
import { describeApiError } from "@/lib/api-client";
import { Link } from "@tanstack/react-router";
import { Package, PackageSearch } from "lucide-react";
import { useActionState, useState } from "react";
import { toast } from "sonner";

const CONSIGNMENT_FLOW: FlowStep[] = [
  { value: "before_consignment", label: "委託前", sub: "Pre-consignment" },
  { value: "consignment_designated", label: "委託指定", sub: "Designated" },
  { value: "consignment_result_entered", label: "結果入力", sub: "Result entered" },
];

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

  if (isLoading) return <p className="page">読み込み中…</p>;
  if (error)
    return (
      <p className="page" style={{ color: "var(--danger)" }}>
        エラー: {describeApiError(error)}
      </p>
    );
  if (!data) return null;
  if (data.caseType !== "consignment") {
    return (
      <p className="page" style={{ color: "var(--danger)" }}>
        この画面は委託販売案件用です（このID は {data.caseType} 案件です）。
      </p>
    );
  }

  // 委託は委託指定前のみロット修正可。
  const editLotsAllowed = data.status === "before_consignment";
  const flowIdx = CONSIGNMENT_FLOW.findIndex((s) => s.value === data.status);

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
    <div className="page">
      <div className="detail-header">
        <div className="detail-header-meta">
          <CaseTypePill caseType="consignment" />
          {data.status && <CaseStatusPill caseType="consignment" status={data.status} />}
          <Pill tone="outline" mono>
            v{data.version}
          </Pill>
        </div>
        <h1>
          委託販売案件 <span className="id">{id}</span>
        </h1>
      </div>

      <StatusFlow steps={CONSIGNMENT_FLOW} currentIndex={flowIdx < 0 ? 0 : flowIdx} />

      <DCard className="mt-6" data-testid="consignment-case-lots">
        <DCardHeader
          title={`対象ロット (${data.lots.length})`}
          icon={<Package className="ico" size={15} />}
          actions={
            editLotsAllowed && (
              <Guard requiredRole="operator">
                <button
                  type="button"
                  className="btn btn-sm btn-ghost"
                  onClick={() => setEditLotsOpen(true)}
                >
                  <PackageSearch className="ico" />
                  ロットを修正
                </button>
              </Guard>
            )
          }
        />
        <DCardBody>
          <div className="col gap-2">
            {data.lots.map((lotNumber) => (
              <Link
                key={lotNumber}
                to="/lots/$id"
                params={{ id: lotNumber }}
                className="row"
                style={{
                  justifyContent: "space-between",
                  padding: "8px 10px",
                  border: "1px solid var(--border-design)",
                  borderRadius: "var(--r-sm)",
                }}
              >
                <span className="row gap-2">
                  <Package size={14} style={{ color: "var(--fg-subtle)" }} />
                  <span className="mono text-sm">{lotNumber}</span>
                </span>
                <span className="text-xs muted">詳細</span>
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
        </DCardBody>
      </DCard>

      <hr className="sep" />

      <Guard
        requiredRole="operator"
        fallback={<p className="muted text-sm">状態遷移には operator 以上のロールが必要です。</p>}
      >
        <div className="grid-2">
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
