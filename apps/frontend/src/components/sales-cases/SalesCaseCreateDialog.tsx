import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
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
  const {
    register,
    handleSubmit,
    setValue,
    watch,
    formState: { errors, isSubmitting },
  } = useForm<SalesCaseCreateModalValues>({
    resolver: zodResolver(salesCaseCreateModalSchema),
    defaultValues: salesCaseCreateModalDefaultValues,
    mode: "onTouched",
  });

  const caseType = watch("caseType");
  const divisionCode = watch("divisionCode");
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
        <form onSubmit={onSubmit} noValidate className="space-y-3">
          <div className="space-y-1">
            <Label>案件種別</Label>
            <Select
              value={caseType}
              onValueChange={(v) =>
                setValue("caseType", v as SalesCaseCreateModalValues["caseType"], {
                  shouldDirty: true,
                  shouldValidate: true,
                })
              }
            >
              <SelectTrigger className="w-full" aria-invalid={!!errors.caseType}>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {CASE_TYPE_OPTIONS.map(([value, label]) => (
                  <SelectItem key={value} value={value}>
                    {label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <FieldError message={errors.caseType?.message} />
          </div>
          <div className="space-y-1">
            <Label>事業部</Label>
            <Select
              value={divisionCode != null ? String(divisionCode) : ""}
              onValueChange={(v) =>
                setValue("divisionCode", Number(v), { shouldDirty: true, shouldValidate: true })
              }
            >
              <SelectTrigger className="w-full" aria-invalid={!!errors.divisionCode}>
                <SelectValue placeholder="事業部を選択" />
              </SelectTrigger>
              <SelectContent>
                {(masters?.divisions ?? []).map((d) => (
                  <SelectItem key={d.code} value={String(d.code)}>
                    {d.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <FieldError message={errors.divisionCode?.message} />
          </div>
          <div className="space-y-1">
            <Label htmlFor="dialog-salesDate">販売日</Label>
            <Input
              id="dialog-salesDate"
              type="date"
              aria-invalid={!!errors.salesDate}
              {...register("salesDate")}
            />
            <FieldError message={errors.salesDate?.message} />
          </div>
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
      </DialogContent>
    </Dialog>
  );
}

function FieldError({ message }: { message?: string }) {
  if (!message) return null;
  return (
    <p role="alert" className="text-destructive text-xs">
      {message}
    </p>
  );
}
