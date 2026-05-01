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
import { useNavigate } from "@tanstack/react-router";
import { useActionState, useState } from "react";
import { toast } from "sonner";

const INSPECTION_RESULT = ["pass", "fail"] as const;

export function LotCreatePage() {
  const navigate = useNavigate();
  const [inspectionResult, setInspectionResult] =
    useState<(typeof INSPECTION_RESULT)[number]>("pass");

  const [, action, isPending] = useActionState(async (_prev: null, fd: FormData) => {
    const get = (k: string) => String(fd.get(k) ?? "");
    const num = (k: string) => Number(get(k));
    try {
      const created = await createLot({
        lotNumber: { year: num("year"), location: get("location"), seq: num("seq") },
        divisionCode: num("divisionCode"),
        departmentCode: num("departmentCode"),
        sectionCode: num("sectionCode"),
        processCategory: num("processCategory"),
        inspectionCategory: num("inspectionCategory"),
        manufacturingCategory: num("manufacturingCategory"),
        details: [
          {
            itemCategory: get("itemCategory"),
            premiumCategory: get("premiumCategory"),
            productCategoryCode: get("productCategoryCode"),
            lengthSpecLower: num("lengthSpecLower"),
            thicknessSpecLower: num("thicknessSpecLower"),
            thicknessSpecUpper: num("thicknessSpecUpper"),
            qualityGrade: get("qualityGrade"),
            count: num("count"),
            quantity: num("quantity"),
            inspectionResultCategory: inspectionResult,
          },
        ],
      });
      toast.success("ロットを作成しました");
      navigate({ to: "/lots/$id", params: { id: created.lotNumber } });
    } catch (e) {
      toast.error(describeApiError(e));
    }
    return null;
  }, null);

  return (
    <Guard
      requiredRole="operator"
      fallback={<p className="text-muted-foreground">作成には operator 以上のロールが必要です。</p>}
    >
      <Card>
        <CardHeader>
          <CardTitle>在庫ロット 新規作成</CardTitle>
        </CardHeader>
        <CardContent>
          <form action={action} className="grid grid-cols-1 gap-4 md:grid-cols-3">
            <Section title="ロット番号">
              <NumberField name="year" label="年度" defaultValue={2026} />
              <TextField name="location" label="保管場所" defaultValue="A" />
              <NumberField name="seq" label="連番" defaultValue={1} />
            </Section>
            <Section title="区分">
              <NumberField name="divisionCode" label="事業部" defaultValue={1} />
              <NumberField name="departmentCode" label="部" defaultValue={1} />
              <NumberField name="sectionCode" label="課" defaultValue={1} />
              <NumberField name="processCategory" label="工程" defaultValue={1} />
              <NumberField name="inspectionCategory" label="検査" defaultValue={1} />
              <NumberField name="manufacturingCategory" label="製造" defaultValue={1} />
            </Section>
            <Section title="明細 (1 件)">
              <TextField name="itemCategory" label="品目区分" defaultValue="general" />
              <TextField name="premiumCategory" label="上位品区分" defaultValue="none" />
              <TextField name="productCategoryCode" label="商品分類コード" defaultValue="default" />
              <NumberField name="lengthSpecLower" label="長さ下限" step="0.01" defaultValue={1} />
              <NumberField name="thicknessSpecLower" label="太さ下限" step="0.01" defaultValue={1} />
              <NumberField name="thicknessSpecUpper" label="太さ上限" step="0.01" defaultValue={2} />
              <TextField name="qualityGrade" label="品質等級" defaultValue="A" />
              <NumberField name="count" label="個数" defaultValue={10} />
              <NumberField name="quantity" label="数量" step="0.01" defaultValue={10} />
              <div className="space-y-1">
                <Label>検査結果</Label>
                <Select
                  value={inspectionResult}
                  onValueChange={(v) => setInspectionResult(v as typeof inspectionResult)}
                >
                  <SelectTrigger>
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="pass">合格</SelectItem>
                    <SelectItem value="fail">不合格</SelectItem>
                  </SelectContent>
                </Select>
              </div>
            </Section>

            <div className="md:col-span-3">
              <Button type="submit" disabled={isPending}>
                {isPending ? "作成中…" : "作成"}
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
  name,
  label,
  defaultValue,
  step,
}: {
  name: string;
  label: string;
  defaultValue?: number;
  step?: string;
}) {
  return (
    <div className="space-y-1">
      <Label htmlFor={name}>{label}</Label>
      <Input id={name} name={name} type="number" defaultValue={defaultValue} step={step} required />
    </div>
  );
}

function TextField({
  name,
  label,
  defaultValue,
}: {
  name: string;
  label: string;
  defaultValue?: string;
}) {
  return (
    <div className="space-y-1">
      <Label htmlFor={name}>{label}</Label>
      <Input id={name} name={name} type="text" defaultValue={defaultValue} required />
    </div>
  );
}
