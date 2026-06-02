import { Button } from "@/components/atoms/button";
import { Form } from "@/components/atoms/form";
import { DCard, DCardBody, DCardHeader, DesignPageHeader } from "@/components/design/primitives";
import { NumberField, SelectField, TextField } from "@/components/molecules";
import { Guard } from "@/components/organisms/auth/Guard";
import { useCodeMasters } from "@/hooks/use-code-masters";
import { createLot } from "@/hooks/use-lot";
import { describeApiError } from "@/lib/api-client";
import { zodResolver } from "@hookform/resolvers/zod";
import { Link, useNavigate } from "@tanstack/react-router";
import { Calendar, Layers, Save, Tag } from "lucide-react";
import type { ReactNode } from "react";
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
  const form = useForm<LotCreateFormValues>({
    resolver: zodResolver(lotCreateFormSchema),
    defaultValues: lotCreateDefaultValues,
    mode: "onTouched",
  });
  const {
    handleSubmit,
    setValue,
    control,
    formState: { isSubmitting },
  } = form;

  const { data: masters } = useCodeMasters();
  const divisionCode = form.watch("divisionCode");
  const departmentCode = form.watch("departmentCode");

  const divisions = masters?.divisions ?? [];
  const departments = (masters?.departments ?? []).filter((d) => d.divisionCode === divisionCode);
  const sections = (masters?.sections ?? []).filter((s) => s.departmentCode === departmentCode);
  const asOptions = (xs: Array<{ code: number; name: string }>): Array<[string, string]> =>
    xs.map((x) => [String(x.code), x.name]);

  // 事業部を変えたら配下の部・課を先頭候補にリセットする（階層の整合性を保つ）。
  const onDivisionAfter = (v: string) => {
    const code = Number(v);
    const firstDept = (masters?.departments ?? []).find((d) => d.divisionCode === code);
    if (firstDept) {
      setValue("departmentCode", firstDept.code, { shouldValidate: true });
      const firstSec = (masters?.sections ?? []).find((s) => s.departmentCode === firstDept.code);
      if (firstSec) setValue("sectionCode", firstSec.code, { shouldValidate: true });
    }
  };
  const onDepartmentAfter = (v: string) => {
    const code = Number(v);
    const firstSec = (masters?.sections ?? []).find((s) => s.departmentCode === code);
    if (firstSec) setValue("sectionCode", firstSec.code, { shouldValidate: true });
  };

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
      fallback={<p className="page muted">作成には operator 以上のロールが必要です。</p>}
    >
      <div className="page">
        <DesignPageHeader
          eyebrow="新規作成"
          title="在庫ロットを作成"
          subtitle="バックエンドの境界値に合わせて入力時に検証します。"
          actions={
            <Link to="/lots" className="btn btn-sm btn-ghost">
              キャンセル
            </Link>
          }
        />

        <Form {...form}>
          <form onSubmit={onSubmit} noValidate className="grid grid-cols-1 gap-4 md:grid-cols-3">
            <Section title="ロット番号" icon={<Tag className="ico" size={15} />}>
              <NumberField control={control} name="year" label="年度" />
              <TextField control={control} name="location" label="保管場所" />
              <NumberField control={control} name="seq" label="連番" />
            </Section>
            <Section title="区分" icon={<Calendar className="ico" size={15} />}>
              <SelectField
                control={control}
                name="divisionCode"
                label="事業部"
                options={asOptions(divisions)}
                parse={Number}
                onAfterChange={onDivisionAfter}
              />
              <SelectField
                control={control}
                name="departmentCode"
                label="部"
                options={asOptions(departments)}
                parse={Number}
                onAfterChange={onDepartmentAfter}
              />
              <SelectField
                control={control}
                name="sectionCode"
                label="課"
                options={asOptions(sections)}
                parse={Number}
              />
              <SelectField
                control={control}
                name="processCategory"
                label="工程"
                options={asOptions(masters?.processCategories ?? [])}
                parse={Number}
              />
              <SelectField
                control={control}
                name="inspectionCategory"
                label="検査"
                options={asOptions(masters?.inspectionCategories ?? [])}
                parse={Number}
              />
              <SelectField
                control={control}
                name="manufacturingCategory"
                label="製造"
                options={asOptions(masters?.manufacturingCategories ?? [])}
                parse={Number}
              />
            </Section>
            <Section title="明細 (1 件)" icon={<Layers className="ico" size={15} />}>
              <SelectField
                control={control}
                name="itemCategory"
                label="品目区分"
                options={[
                  ["general", "通常"],
                  ["premium", "上位品"],
                  ["custom", "特注"],
                ]}
              />
              <TextField control={control} name="premiumCategory" label="上位品区分" />
              <TextField control={control} name="productCategoryCode" label="商品分類コード" />
              <NumberField control={control} name="lengthSpecLower" label="長さ下限" step="0.01" />
              <NumberField
                control={control}
                name="thicknessSpecLower"
                label="太さ下限"
                step="0.01"
              />
              <NumberField
                control={control}
                name="thicknessSpecUpper"
                label="太さ上限"
                step="0.01"
              />
              <TextField control={control} name="qualityGrade" label="品質等級" />
              <NumberField control={control} name="count" label="個数" />
              <NumberField control={control} name="quantity" label="数量" step="0.001" />
              <SelectField
                control={control}
                name="inspectionResultCategory"
                label="検査結果"
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
        </Form>
      </div>
    </Guard>
  );
}

function Section({
  title,
  icon,
  children,
}: {
  title: string;
  icon?: ReactNode;
  children: ReactNode;
}) {
  return (
    <DCard>
      <DCardHeader title={title} icon={icon} />
      <DCardBody>
        <div className="space-y-2">{children}</div>
      </DCardBody>
    </DCard>
  );
}
