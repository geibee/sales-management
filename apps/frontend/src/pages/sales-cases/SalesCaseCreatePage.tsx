import { Button } from "@/components/atoms/button";
import { Form } from "@/components/atoms/form";
import { DCard, DCardBody, DCardHeader, DesignPageHeader } from "@/components/design/primitives";
import { FieldError, SelectField, TextField } from "@/components/molecules";
import { Guard } from "@/components/organisms/auth/Guard";
import { LotSelectDialog } from "@/components/organisms/dialogs/LotSelectDialog";
import { useCodeMasters } from "@/hooks/use-code-masters";
import { createSalesCase } from "@/hooks/use-sales-case";
import { describeApiError } from "@/lib/api-client";
import { zodResolver } from "@hookform/resolvers/zod";
import { Link, useNavigate } from "@tanstack/react-router";
import { Box, Calendar, Check, PackageSearch, Save, Tag, X } from "lucide-react";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { toast } from "sonner";
import {
  type SalesCaseCreateFormValues,
  caseDetailRoute,
  salesCaseCreateDefaultValues,
  salesCaseCreateFormSchema,
  toCreateSalesCaseBody,
} from "./sales-case-create-validation";

const CASE_TYPE_CARDS = [
  { v: "direct", l: "直接販売", d: "査定→契約→出荷の通常フロー" },
  { v: "reservation", l: "予約", d: "予約→確定→引渡のフロー" },
  { v: "consignment", l: "委託", d: "委託指定→結果入力のフロー" },
] as const;

export function SalesCaseCreatePage() {
  const navigate = useNavigate();
  const form = useForm<SalesCaseCreateFormValues>({
    resolver: zodResolver(salesCaseCreateFormSchema),
    defaultValues: salesCaseCreateDefaultValues,
    mode: "onTouched",
  });
  const {
    handleSubmit,
    setValue,
    control,
    formState: { errors, isSubmitting },
  } = form;

  const lots = form.watch("lots");
  const caseType = form.watch("caseType");
  const { data: masters } = useCodeMasters();
  const [lotDialogOpen, setLotDialogOpen] = useState(false);

  const onSubmit = handleSubmit(async (values) => {
    try {
      const created = await createSalesCase(toCreateSalesCaseBody(values));
      toast.success("案件を作成しました");
      navigate({ to: caseDetailRoute(values.caseType), params: { id: created.salesCaseNumber } });
    } catch (e) {
      toast.error(describeApiError(e));
    }
  });

  return (
    <Guard
      requiredRole="operator"
      fallback={<p className="page muted">作成には operator 以上のロールが必要です。</p>}
    >
      <div className="page">
        <DesignPageHeader
          eyebrow="新規作成"
          title="販売案件を作成"
          subtitle="製造完了済みロットを指定して販売案件を起票します。"
          actions={
            <Link to="/sales-cases" className="btn btn-sm btn-ghost">
              キャンセル
            </Link>
          }
        />

        <Form {...form}>
          <form onSubmit={onSubmit} noValidate>
            <DCard className="mb-4">
              <DCardHeader title="案件種別" icon={<Tag className="ico" size={15} />} />
              <DCardBody>
                {/* caseType は RHF defaultValues に存在し、下のカードから setValue で更新する */}
                <div className="grid-3">
                  {CASE_TYPE_CARDS.map((t) => {
                    const on = caseType === t.v;
                    return (
                      <button
                        type="button"
                        key={t.v}
                        onClick={() =>
                          setValue("caseType", t.v, { shouldDirty: true, shouldValidate: true })
                        }
                        style={{
                          textAlign: "left",
                          padding: 14,
                          border: `1.5px solid ${on ? "var(--accent-design)" : "var(--border-design)"}`,
                          background: on ? "var(--accent-soft)" : "var(--bg-elev)",
                          borderRadius: "var(--r-md)",
                          cursor: "pointer",
                          display: "flex",
                          flexDirection: "column",
                          gap: 4,
                        }}
                      >
                        <span className="row" style={{ justifyContent: "space-between" }}>
                          <span style={{ fontWeight: 600, fontSize: 13.5 }}>{t.l}</span>
                          {on && <Check size={14} style={{ color: "var(--accent-design)" }} />}
                        </span>
                        <span className="text-xs muted">{t.d}</span>
                      </button>
                    );
                  })}
                </div>
              </DCardBody>
            </DCard>

            <div className="split-2">
              <DCard>
                <DCardHeader title="基本情報" icon={<Calendar className="ico" size={15} />} />
                <DCardBody>
                  <SelectField
                    control={control}
                    name="divisionCode"
                    label="事業部"
                    options={(masters?.divisions ?? []).map(
                      (d) => [String(d.code), d.name] as [string, string],
                    )}
                    parse={Number}
                    placeholder="事業部を選択"
                  />
                  <div className="mt-3">
                    <TextField control={control} name="salesDate" label="販売日" type="date" />
                  </div>
                </DCardBody>
              </DCard>

              <DCard>
                <DCardHeader
                  title={`対象ロット (${lots.length})`}
                  icon={<Box className="ico" size={15} />}
                  actions={
                    <button
                      type="button"
                      className="btn btn-sm btn-ghost"
                      onClick={() => setLotDialogOpen(true)}
                    >
                      <PackageSearch className="ico" />
                      ロットを選択
                    </button>
                  }
                />
                <DCardBody>
                  <p className="text-sm muted mb-3">
                    「製造完了」状態のロットのみ販売案件に紐付けできます。
                  </p>
                  {lots.length === 0 ? (
                    <p className="text-sm muted">ロットが選択されていません</p>
                  ) : (
                    <div className="col gap-2">
                      {lots.map((lotNumber) => (
                        <div
                          key={lotNumber}
                          className="row"
                          style={{
                            justifyContent: "space-between",
                            padding: "8px 10px",
                            border: "1px solid var(--border-design)",
                            borderRadius: "var(--r-sm)",
                          }}
                        >
                          <span className="row gap-2">
                            <Box size={14} style={{ color: "var(--fg-subtle)" }} />
                            <span className="mono text-sm">{lotNumber}</span>
                          </span>
                          <button
                            type="button"
                            className="icon-btn"
                            aria-label={`ロット ${lotNumber} を除外`}
                            onClick={() =>
                              setValue(
                                "lots",
                                lots.filter((x) => x !== lotNumber),
                                { shouldDirty: true, shouldValidate: true },
                              )
                            }
                          >
                            <X size={13} />
                          </button>
                        </div>
                      ))}
                    </div>
                  )}
                  <FieldError message={errors.lots?.message} />
                  <LotSelectDialog
                    open={lotDialogOpen}
                    onOpenChange={setLotDialogOpen}
                    value={lots}
                    onConfirm={(picked) =>
                      setValue("lots", picked, { shouldDirty: true, shouldValidate: true })
                    }
                    title="対象ロットを選択"
                  />
                </DCardBody>
              </DCard>
            </div>

            <div className="row mt-4" style={{ justifyContent: "flex-end" }}>
              <Button type="submit" disabled={isSubmitting}>
                <Save className="size-4" />
                {isSubmitting ? "作成中…" : "作成"}
              </Button>
            </div>
          </form>
        </Form>
      </div>
    </Guard>
  );
}
