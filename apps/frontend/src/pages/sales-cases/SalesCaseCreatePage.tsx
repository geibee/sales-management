import { Button } from "@/components/atoms/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/atoms/card";
import { Form } from "@/components/atoms/form";
import { Label } from "@/components/atoms/label";
import { FieldError, SelectField, TextField } from "@/components/molecules";
import { Guard } from "@/components/organisms/auth/Guard";
import { LotSelectDialog } from "@/components/organisms/dialogs/LotSelectDialog";
import { useCodeMasters } from "@/hooks/use-code-masters";
import { createSalesCase } from "@/hooks/use-sales-case";
import { describeApiError } from "@/lib/api-client";
import { zodResolver } from "@hookform/resolvers/zod";
import { Link, useNavigate } from "@tanstack/react-router";
import { ArrowLeft, BriefcaseBusiness, PackageSearch, Save } from "lucide-react";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { toast } from "sonner";
import {
  CASE_TYPE_OPTIONS,
  type SalesCaseCreateFormValues,
  caseDetailRoute,
  salesCaseCreateDefaultValues,
  salesCaseCreateFormSchema,
  toCreateSalesCaseBody,
} from "./sales-case-create-validation";

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
      fallback={<p className="text-muted-foreground">作成には operator 以上のロールが必要です。</p>}
    >
      <Card className="rounded-lg">
        <CardHeader>
          <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
            <div className="space-y-1">
              <CardTitle className="flex items-center gap-2 text-xl">
                <BriefcaseBusiness className="size-5" />
                販売案件 新規作成
              </CardTitle>
              <p className="text-muted-foreground text-sm">
                製造完了済みロットを指定して販売案件を起票します。
              </p>
            </div>
            <Button asChild variant="outline" size="sm">
              <Link to="/sales-cases">
                <ArrowLeft className="size-4" />
                一覧
              </Link>
            </Button>
          </div>
        </CardHeader>
        <CardContent>
          <Form {...form}>
            <form
              onSubmit={onSubmit}
              noValidate
              className="grid grid-cols-1 gap-4 lg:grid-cols-[1.2fr_0.8fr]"
            >
              <SelectField
                control={control}
                name="caseType"
                label="案件種別"
                options={CASE_TYPE_OPTIONS}
              />
              <div className="space-y-2 lg:row-span-3">
                <div className="flex items-center justify-between gap-2">
                  <Label>対象ロット</Label>
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    onClick={() => setLotDialogOpen(true)}
                  >
                    <PackageSearch className="size-4" />
                    ロットを選択
                  </Button>
                </div>
                {lots.length === 0 ? (
                  <p className="text-muted-foreground text-sm">ロットが選択されていません</p>
                ) : (
                  <div className="flex flex-wrap gap-1.5">
                    {lots.map((lotNumber) => (
                      <span
                        key={lotNumber}
                        className="rounded-md border bg-muted/40 px-2 py-1 font-mono text-xs"
                      >
                        {lotNumber}
                      </span>
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
              </div>
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
              <TextField control={control} name="salesDate" label="販売日" type="date" />
              <div className="flex items-center justify-end lg:col-span-2">
                <Button type="submit" disabled={isSubmitting}>
                  <Save className="size-4" />
                  {isSubmitting ? "作成中…" : "作成"}
                </Button>
              </div>
            </form>
          </Form>
        </CardContent>
      </Card>
    </Guard>
  );
}
