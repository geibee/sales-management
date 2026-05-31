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
import { createLot } from "@/hooks/use-lot";
import { describeApiError } from "@/lib/api-client";
import { zodResolver } from "@hookform/resolvers/zod";
import { Link, useNavigate } from "@tanstack/react-router";
import { ArrowLeft, PackagePlus, Save } from "lucide-react";
import { type UseFormRegisterReturn, useForm } from "react-hook-form";
import { toast } from "sonner";
import {
  type LotCreateFormValues,
  lotCreateDefaultValues,
  lotCreateFormSchema,
  toCreateLotBody,
} from "./lot-create-validation";

export function LotCreatePage() {
  const navigate = useNavigate();
  const {
    register,
    handleSubmit,
    setValue,
    watch,
    formState: { errors, isSubmitting },
  } = useForm<LotCreateFormValues>({
    resolver: zodResolver(lotCreateFormSchema),
    defaultValues: lotCreateDefaultValues,
    mode: "onTouched",
  });

  const itemCategory = watch("itemCategory");
  const inspectionResultCategory = watch("inspectionResultCategory");

  const onSubmit = handleSubmit(async (values) => {
    try {
      const created = await createLot(toCreateLotBody(values));
      toast.success("ロットを作成しました");
      navigate({ to: "/lots/$id", params: { id: created.lotNumber } });
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
                <PackagePlus className="size-5" />
                在庫ロット 新規作成
              </CardTitle>
              <p className="text-muted-foreground text-sm">
                バックエンドの境界値に合わせて入力時に検証します。
              </p>
            </div>
            <Button asChild variant="outline" size="sm">
              <Link to="/lots">
                <ArrowLeft className="size-4" />
                一覧
              </Link>
            </Button>
          </div>
        </CardHeader>
        <CardContent>
          <form onSubmit={onSubmit} noValidate className="grid grid-cols-1 gap-4 md:grid-cols-3">
            <Section title="ロット番号">
              <NumberField
                label="年度"
                registration={register("year")}
                error={errors.year?.message}
              />
              <TextField
                label="保管場所"
                registration={register("location")}
                error={errors.location?.message}
              />
              <NumberField
                label="連番"
                registration={register("seq")}
                error={errors.seq?.message}
              />
            </Section>
            <Section title="区分">
              <NumberField
                label="事業部"
                registration={register("divisionCode")}
                error={errors.divisionCode?.message}
              />
              <NumberField
                label="部"
                registration={register("departmentCode")}
                error={errors.departmentCode?.message}
              />
              <NumberField
                label="課"
                registration={register("sectionCode")}
                error={errors.sectionCode?.message}
              />
              <NumberField
                label="工程"
                registration={register("processCategory")}
                error={errors.processCategory?.message}
              />
              <NumberField
                label="検査"
                registration={register("inspectionCategory")}
                error={errors.inspectionCategory?.message}
              />
              <NumberField
                label="製造"
                registration={register("manufacturingCategory")}
                error={errors.manufacturingCategory?.message}
              />
            </Section>
            <Section title="明細 (1 件)">
              <SelectField
                label="品目区分"
                value={itemCategory}
                error={errors.itemCategory?.message}
                onValueChange={(value) =>
                  setValue("itemCategory", value as LotCreateFormValues["itemCategory"], {
                    shouldDirty: true,
                    shouldValidate: true,
                  })
                }
                options={[
                  ["general", "通常"],
                  ["premium", "上位品"],
                  ["custom", "特注"],
                ]}
              />
              <TextField
                label="上位品区分"
                registration={register("premiumCategory")}
                error={errors.premiumCategory?.message}
              />
              <TextField
                label="商品分類コード"
                registration={register("productCategoryCode")}
                error={errors.productCategoryCode?.message}
              />
              <NumberField
                label="長さ下限"
                step="0.01"
                registration={register("lengthSpecLower")}
                error={errors.lengthSpecLower?.message}
              />
              <NumberField
                label="太さ下限"
                step="0.01"
                registration={register("thicknessSpecLower")}
                error={errors.thicknessSpecLower?.message}
              />
              <NumberField
                label="太さ上限"
                step="0.01"
                registration={register("thicknessSpecUpper")}
                error={errors.thicknessSpecUpper?.message}
              />
              <TextField
                label="品質等級"
                registration={register("qualityGrade")}
                error={errors.qualityGrade?.message}
              />
              <NumberField
                label="個数"
                registration={register("count")}
                error={errors.count?.message}
              />
              <NumberField
                label="数量"
                step="0.001"
                registration={register("quantity")}
                error={errors.quantity?.message}
              />
              <SelectField
                label="検査結果"
                value={inspectionResultCategory}
                error={errors.inspectionResultCategory?.message}
                onValueChange={(value) =>
                  setValue(
                    "inspectionResultCategory",
                    value as LotCreateFormValues["inspectionResultCategory"],
                    {
                      shouldDirty: true,
                      shouldValidate: true,
                    },
                  )
                }
                options={[
                  ["pass", "合格"],
                  ["fail", "不合格"],
                ]}
              />
            </Section>

            <div className="flex items-center justify-end md:col-span-3">
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

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="space-y-2 rounded-lg border p-4">
      <p className="font-medium text-sm">{title}</p>
      <div className="space-y-2">{children}</div>
    </div>
  );
}

function NumberField({
  label,
  registration,
  error,
  step,
}: {
  label: string;
  registration: UseFormRegisterReturn;
  error?: string;
  step?: string;
}) {
  return (
    <div className="space-y-1">
      <Label htmlFor={registration.name}>{label}</Label>
      <Input
        id={registration.name}
        type="number"
        step={step}
        aria-invalid={!!error}
        {...registration}
      />
      <FieldError message={error} />
    </div>
  );
}

function TextField({
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
      <Input id={registration.name} type="text" aria-invalid={!!error} {...registration} />
      <FieldError message={error} />
    </div>
  );
}

function SelectField({
  label,
  value,
  options,
  onValueChange,
  error,
}: {
  label: string;
  value: string;
  options: Array<[string, string]>;
  onValueChange: (value: string) => void;
  error?: string;
}) {
  return (
    <div className="space-y-1">
      <Label>{label}</Label>
      <Select value={value} onValueChange={onValueChange}>
        <SelectTrigger className="w-full" aria-invalid={!!error}>
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          {options.map(([optionValue, optionLabel]) => (
            <SelectItem key={optionValue} value={optionValue}>
              {optionLabel}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
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
