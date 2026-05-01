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
import { useNavigate } from "@tanstack/react-router";
import { useActionState, useState } from "react";
import { toast } from "sonner";

const CASE_TYPES = ["direct", "reservation", "consignment"] as const;

export function SalesCaseCreatePage() {
  const navigate = useNavigate();
  const [caseType, setCaseType] = useState<(typeof CASE_TYPES)[number]>("direct");

  const [, action, isPending] = useActionState(async (_prev: null, fd: FormData) => {
    const lots = String(fd.get("lots") ?? "")
      .split(",")
      .map((s) => s.trim())
      .filter(Boolean);
    if (lots.length === 0) {
      toast.error("ロット ID を1つ以上入力してください");
      return null;
    }
    try {
      const created = await createSalesCase({
        lots,
        divisionCode: Number(fd.get("divisionCode") ?? 1),
        salesDate: String(fd.get("salesDate") ?? ""),
        caseType,
      });
      toast.success("案件を作成しました");
      const target =
        caseType === "reservation"
          ? "/reservation-cases/$id"
          : caseType === "consignment"
            ? "/consignment-cases/$id"
            : "/sales-cases/$id";
      navigate({ to: target, params: { id: created.salesCaseNumber } });
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
          <CardTitle>販売案件 新規作成</CardTitle>
        </CardHeader>
        <CardContent>
          <form action={action} className="space-y-4">
            <div className="space-y-1">
              <Label>案件種別</Label>
              <Select value={caseType} onValueChange={(v) => setCaseType(v as typeof caseType)}>
                <SelectTrigger>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="direct">直接販売 (direct)</SelectItem>
                  <SelectItem value="reservation">予約 (reservation)</SelectItem>
                  <SelectItem value="consignment">委託 (consignment)</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-1">
              <Label htmlFor="lots">ロット ID (カンマ区切り)</Label>
              <Input id="lots" name="lots" placeholder="2026-A-1,2026-A-2" required />
            </div>
            <div className="space-y-1">
              <Label htmlFor="divisionCode">事業部コード</Label>
              <Input id="divisionCode" name="divisionCode" type="number" defaultValue={1} />
            </div>
            <div className="space-y-1">
              <Label htmlFor="salesDate">販売日</Label>
              <Input id="salesDate" name="salesDate" type="date" required />
            </div>
            <Button type="submit" disabled={isPending}>
              {isPending ? "作成中…" : "作成"}
            </Button>
          </form>
        </CardContent>
      </Card>
    </Guard>
  );
}
