import { Button } from "@/components/atoms/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/atoms/card";
import { Guard } from "@/components/auth/Guard";
import { NumberField, SelectField, TextField } from "@/components/molecules";
import { useCodeMasters } from "@/hooks/use-code-masters";
import { createLot } from "@/hooks/use-lot";
import { describeApiError } from "@/lib/api-client";
import { zodResolver } from "@hookform/resolvers/zod";
import { Link, useNavigate } from "@tanstack/react-router";
import { ArrowLeft, PackagePlus, Save } from "lucide-react";
import { useForm } from "react-hook-form";
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

  const { data: masters } = useCodeMasters();
  const divisionCode = watch("divisionCode");
  const departmentCode = watch("departmentCode");

  const divisions = masters?.divisions ?? [];
  const departments = (masters?.departments ?? []).filter((d) => d.divisionCode === divisionCode);
  const sections = (masters?.sections ?? []).filter((s) => s.departmentCode === departmentCode);
  const asOptions = (xs: Array<{ code: number; name: string }>): Array<[string, string]> =>
    xs.map((x) => [String(x.code), x.name]);

  // 事業部を変えたら配下の部・課を先頭候補にリセットする（階層の整合性を保つ）。
  const onDivisionChange = (v: string) => {
    const code = Number(v);
    setValue("divisionCode", code, { shouldValidate: true, shouldDirty: true });
    const firstDept = (masters?.departments ?? []).find((d) => d.divisionCode === code);
    if (firstDept) {
      setValue("departmentCode", firstDept.code, { shouldValidate: true });
      const firstSec = (masters?.sections ?? []).find((s) => s.departmentCode === firstDept.code);
      if (firstSec) setValue("sectionCode", firstSec.code, { shouldValidate: true });
    }
  };

  const onDepartmentChange = (v: string) => {
    const code = Number(v);
    setValue("departmentCode", code, { shouldValidate: true, shouldDirty: true });
    const firstSec = (masters?.sections ?? []).find((s) => s.departmentCode === code);
    if (firstSec) setValue("sectionCode", firstSec.code, { shouldValidate: true });
  };

  const setCode =
    (field: "sectionCode" | "processCategory" | "inspectionCategory" | "manufacturingCategory") =>
    (v: string) =>
      setValue(field, Number(v), { shouldValidate: true, shouldDirty: true });

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
              <SelectField
                label="事業部"
                value={String(divisionCode)}
                options={asOptions(divisions)}
                onValueChange={onDivisionChange}
                error={errors.divisionCode?.message}
              />
              <SelectField
                label="部"
                value={String(departmentCode)}
                options={asOptions(departments)}
                onValueChange={onDepartmentChange}
                error={errors.departmentCode?.message}
              />
              <SelectField
                label="課"
                value={String(watch("sectionCode"))}
                options={asOptions(sections)}
                onValueChange={setCode("sectionCode")}
                error={errors.sectionCode?.message}
              />
              <SelectField
                label="工程"
                value={String(watch("processCategory"))}
                options={asOptions(masters?.processCategories ?? [])}
                onValueChange={setCode("processCategory")}
                error={errors.processCategory?.message}
              />
              <SelectField
                label="検査"
                value={String(watch("inspectionCategory"))}
                options={asOptions(masters?.inspectionCategories ?? [])}
                onValueChange={setCode("inspectionCategory")}
                error={errors.inspectionCategory?.message}
              />
              <SelectField
                label="製造"
                value={String(watch("manufacturingCategory"))}
                options={asOptions(masters?.manufacturingCategories ?? [])}
                onValueChange={setCode("manufacturingCategory")}
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
