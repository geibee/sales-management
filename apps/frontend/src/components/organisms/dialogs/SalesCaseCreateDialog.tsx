import { Button } from "@/components/atoms/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/atoms/dialog";
import { Form } from "@/components/atoms/form";
import { SelectField, TextField } from "@/components/molecules";
import { useCodeMasters } from "@/hooks/use-code-masters";
import { createSalesCase } from "@/hooks/use-sales-case";
import { describeApiError } from "@/lib/api-client";
import {
  CASE_TYPE_OPTIONS,
  type SalesCaseCreateModalValues,
  caseDetailRoute,
  salesCaseCreateModalDefaultValues,
  salesCaseCreateModalSchema,
} from "@/pages/sales-cases/sales-case-create-validation";
import { zodResolver } from "@hookform/resolvers/zod";
import { useNavigate } from "@tanstack/react-router";
import { Save } from "lucide-react";
import { useForm } from "react-hook-form";
import { toast } from "sonner";

/**
 * 選択済みロットから販売案件を起票するモーダル。
 * ロット一覧の複数選択 → 「販売案件新規登録」から起動する。
 */
export function SalesCaseCreateDialog({
  open,
  onOpenChange,
  lotNumbers,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  lotNumbers: string[];
}) {
  const navigate = useNavigate();
  const form = useForm<SalesCaseCreateModalValues>({
    resolver: zodResolver(salesCaseCreateModalSchema),
    defaultValues: salesCaseCreateModalDefaultValues,
    mode: "onTouched",
  });
  const {
    handleSubmit,
    control,
    formState: { isSubmitting },
  } = form;
  const { data: masters } = useCodeMasters();

  const onSubmit = handleSubmit(async (values) => {
    try {
      const created = await createSalesCase({
        lots: lotNumbers,
        divisionCode: values.divisionCode,
        salesDate: values.salesDate,
        caseType: values.caseType,
      });
      toast.success("案件を作成しました");
      navigate({ to: caseDetailRoute(values.caseType), params: { id: created.salesCaseNumber } });
    } catch (e) {
      toast.error(describeApiError(e));
    }
  });

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>販売案件 新規作成</DialogTitle>
          <DialogDescription>
            選択した {lotNumbers.length} 件のロットで販売案件を起票します。
          </DialogDescription>
        </DialogHeader>
        <Form {...form}>
          <form onSubmit={onSubmit} noValidate className="space-y-3">
            <SelectField
              control={control}
              name="caseType"
              label="案件種別"
              options={CASE_TYPE_OPTIONS}
            />
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
            <DialogFooter>
              <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
                キャンセル
              </Button>
              <Button type="submit" disabled={isSubmitting || lotNumbers.length === 0}>
                <Save className="size-4" />
                {isSubmitting ? "作成中…" : "作成"}
              </Button>
            </DialogFooter>
          </form>
        </Form>
      </DialogContent>
    </Dialog>
  );
}
