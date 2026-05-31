import { Guard } from "@/components/auth/Guard";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { createSalesCase } from "@/hooks/use-sales-case";
import { describeApiError } from "@/lib/api-client";
import { zodResolver } from "@hookform/resolvers/zod";
import { Link, useNavigate } from "@tanstack/react-router";
import { ArrowLeft, BriefcaseBusiness, Save } from "lucide-react";
import { type UseFormRegisterReturn, useForm } from "react-hook-form";
import { toast } from "sonner";
import {
  type SalesCaseCreateFormValues,
  salesCaseCreateDefaultValues,
  salesCaseCreateFormSchema,
  toCreateSalesCaseBody,
} from "./sales-case-create-validation";

const CASE_TYPE_OPTIONS: Array<[SalesCaseCreateFormValues["caseType"], string]> = [
  ["direct", "直接販売"],
  ["reservation", "予約"],
  ["consignment", "委託"],
];

export function SalesCaseCreatePage() {
  const navigate = useNavigate();
  const {
    register,
    handleSubmit,
    setValue,
    watch,
    formState: { errors, isSubmitting },
  } = useForm<SalesCaseCreateFormValues>({
    resolver: zodResolver(salesCaseCreateFormSchema),
    defaultValues: salesCaseCreateDefaultValues,
    mode: "onTouched",
  });

  const caseType = watch("caseType");

  const onSubmit = handleSubmit(async (values) => {
    try {
      const created = await createSalesCase(toCreateSalesCaseBody(values));
      toast.success("案件を作成しました");
      const target =
        values.caseType === "reservation"
          ? "/reservation-cases/$id"
          : values.caseType === "consignment"
            ? "/consignment-cases/$id"
            : "/sales-cases/$id";
      navigate({ to: target, params: { id: created.salesCaseNumber } });
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
          <form
            onSubmit={onSubmit}
            noValidate
            className="grid grid-cols-1 gap-4 lg:grid-cols-[1.2fr_0.8fr]"
          >
            <div className="space-y-1">
              <Label>案件種別</Label>
              <Select
                value={caseType}
                onValueChange={(value) =>
                  setValue("caseType", value as SalesCaseCreateFormValues["caseType"], {
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
            <div className="space-y-1 lg:row-span-3">
              <Label htmlFor="lotsText">ロット ID</Label>
              <textarea
                id="lotsText"
                rows={7}
                placeholder="2026-A-001, 2026-A-002"
                aria-invalid={!!errors.lotsText}
                className="w-full rounded-md border border-input bg-transparent px-3 py-2 font-mono text-sm shadow-xs outline-none transition-[color,box-shadow] placeholder:text-muted-foreground focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50 aria-invalid:border-destructive aria-invalid:ring-destructive/20"
                {...register("lotsText")}
              />
              <FieldError message={errors.lotsText?.message} />
            </div>
            <NumberField
              label="事業部コード"
              registration={register("divisionCode")}
              error={errors.divisionCode?.message}
            />
            <TextField
              label="販売日"
              type="date"
              registration={register("salesDate")}
              error={errors.salesDate?.message}
            />
            <div className="flex items-center justify-end lg:col-span-2">
              <Button type="submit" disabled={isSubmitting}>
                <Save className="size-4" />
                {isSubmitting ? "作成中…" : "作成"}
              </Button>
            </div>
          </form>
        </CardContent>
      </Card>
    </Guard>
  );
}

function NumberField({
  label,
  registration,
  error,
}: {
  label: string;
  registration: UseFormRegisterReturn;
  error?: string;
}) {
  return (
    <div className="space-y-1">
      <Label htmlFor={registration.name}>{label}</Label>
      <Input id={registration.name} type="number" aria-invalid={!!error} {...registration} />
      <FieldError message={error} />
    </div>
  );
}

function TextField({
  label,
  registration,
  error,
  type = "text",
}: {
  label: string;
  registration: UseFormRegisterReturn;
  error?: string;
  type?: string;
}) {
  return (
    <div className="space-y-1">
      <Label htmlFor={registration.name}>{label}</Label>
      <Input id={registration.name} type={type} aria-invalid={!!error} {...registration} />
      <FieldError message={error} />
    </div>
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
