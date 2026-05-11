import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { useSalesCasesList } from "@/hooks/use-sales-cases-list";
import { describeApiError } from "@/lib/api-client";
import { caseStatusLabel } from "@/lib/format";
import { Link } from "@tanstack/react-router";
import { useState } from "react";

const PAGE_SIZE = 20;
const CASE_TYPE_OPTIONS = ["all", "direct", "reservation", "consignment"] as const;
const CASE_TYPE_LABEL: Record<string, string> = {
  direct: "直接販売",
  reservation: "予約",
  consignment: "委託",
};

const STATUS_OPTIONS = [
  "all",
  "before_appraisal",
  "appraised",
  "contracted",
  "shipping_instructed",
  "shipping_completed",
  "before_reservation",
  "reserved",
  "reservation_confirmed",
  "reservation_delivered",
  "before_consignment",
  "consignment_designated",
  "consignment_result_entered",
] as const;

function detailRouteFor(
  caseType: string,
): "/sales-cases/$id" | "/reservation-cases/$id" | "/consignment-cases/$id" {
  if (caseType === "reservation") return "/reservation-cases/$id";
  if (caseType === "consignment") return "/consignment-cases/$id";
  return "/sales-cases/$id";
}

export function SalesCaseListPage() {
  const [status, setStatus] = useState<string>("all");
  const [caseType, setCaseType] = useState<string>("all");
  const [offset, setOffset] = useState(0);
  const { data, error, isLoading } = useSalesCasesList({
    status,
    caseType,
    limit: PAGE_SIZE,
    offset,
  });

  const total = data?.total ?? 0;
  const items = data?.items ?? [];
  const page = Math.floor(offset / PAGE_SIZE) + 1;
  const lastPage = Math.max(1, Math.ceil(total / PAGE_SIZE));

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="font-semibold text-2xl">販売案件一覧</h1>
        <Link to="/sales-cases/new" className="text-sm font-medium underline underline-offset-4">
          新規作成
        </Link>
      </div>

      <div className="flex flex-wrap items-center gap-3">
        <span className="text-muted-foreground text-sm">種別:</span>
        <Select
          value={caseType}
          onValueChange={(v) => {
            setCaseType(v);
            setOffset(0);
          }}
        >
          <SelectTrigger size="sm" className="w-40">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {CASE_TYPE_OPTIONS.map((c) => (
              <SelectItem key={c} value={c}>
                {c === "all" ? "(すべて)" : CASE_TYPE_LABEL[c]}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>

        <span className="text-muted-foreground text-sm">状態:</span>
        <Select
          value={status}
          onValueChange={(v) => {
            setStatus(v);
            setOffset(0);
          }}
        >
          <SelectTrigger size="sm" className="w-56">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {STATUS_OPTIONS.map((s) => (
              <SelectItem key={s} value={s}>
                {s === "all"
                  ? "(すべて)"
                  : caseStatusLabel(caseType === "all" ? null : caseType, s)}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>

        <span className="text-muted-foreground text-xs">{total} 件</span>
      </div>

      {error && <p className="text-destructive text-sm">エラー: {describeApiError(error)}</p>}

      <div className="rounded-md border">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>案件番号</TableHead>
              <TableHead>種別</TableHead>
              <TableHead>状態</TableHead>
              <TableHead>販売日</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {isLoading && items.length === 0 ? (
              <TableRow>
                <TableCell colSpan={4} className="text-muted-foreground">
                  読み込み中…
                </TableCell>
              </TableRow>
            ) : items.length === 0 ? (
              <TableRow>
                <TableCell colSpan={4} className="text-muted-foreground">
                  該当する案件がありません
                </TableCell>
              </TableRow>
            ) : (
              items.map((it) => (
                <TableRow key={it.salesCaseNumber}>
                  <TableCell>
                    <Link
                      to={detailRouteFor(it.caseType)}
                      params={{ id: it.salesCaseNumber }}
                      className="font-mono underline underline-offset-4"
                    >
                      {it.salesCaseNumber}
                    </Link>
                  </TableCell>
                  <TableCell>{CASE_TYPE_LABEL[it.caseType] ?? it.caseType}</TableCell>
                  <TableCell>
                    <Badge variant="secondary">{caseStatusLabel(it.caseType, it.status)}</Badge>
                  </TableCell>
                  <TableCell>{it.salesDate ?? "—"}</TableCell>
                </TableRow>
              ))
            )}
          </TableBody>
        </Table>
      </div>

      <div className="flex items-center justify-end gap-2">
        <span className="text-muted-foreground text-sm">
          {page} / {lastPage}
        </span>
        <Button
          variant="outline"
          size="sm"
          disabled={offset === 0}
          onClick={() => setOffset(Math.max(0, offset - PAGE_SIZE))}
        >
          前へ
        </Button>
        <Button
          variant="outline"
          size="sm"
          disabled={offset + PAGE_SIZE >= total}
          onClick={() => setOffset(offset + PAGE_SIZE)}
        >
          次へ
        </Button>
      </div>
    </div>
  );
}
