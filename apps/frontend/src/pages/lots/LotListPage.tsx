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
import { useLotsList } from "@/hooks/use-lots-list";
import { describeApiError } from "@/lib/api-client";
import { lotStatusLabel } from "@/lib/format";
import { Link } from "@tanstack/react-router";
import { useState } from "react";

const PAGE_SIZE = 20;
const STATUS_OPTIONS = [
  "all",
  "manufacturing",
  "manufactured",
  "shipping_instructed",
  "shipped",
  "conversion_instructed",
] as const;

export function LotListPage() {
  const [status, setStatus] = useState<string>("all");
  const [offset, setOffset] = useState(0);
  const { data, error, isLoading } = useLotsList({ status, limit: PAGE_SIZE, offset });

  const total = data?.total ?? 0;
  const items = data?.items ?? [];
  const page = Math.floor(offset / PAGE_SIZE) + 1;
  const lastPage = Math.max(1, Math.ceil(total / PAGE_SIZE));

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="font-semibold text-2xl">在庫ロット一覧</h1>
        <Link to="/lots/new" className="text-sm font-medium underline underline-offset-4">
          新規作成
        </Link>
      </div>

      <div className="flex items-center gap-3">
        <span className="text-muted-foreground text-sm">状態:</span>
        <Select
          value={status}
          onValueChange={(v) => {
            setStatus(v);
            setOffset(0);
          }}
        >
          <SelectTrigger size="sm" className="w-48">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {STATUS_OPTIONS.map((s) => (
              <SelectItem key={s} value={s}>
                {s === "all" ? "(すべて)" : lotStatusLabel(s)}
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
              <TableHead>ロット番号</TableHead>
              <TableHead>状態</TableHead>
              <TableHead>製造完了日</TableHead>
              <TableHead className="text-right">version</TableHead>
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
                  該当するロットがありません
                </TableCell>
              </TableRow>
            ) : (
              items.map((it) => (
                <TableRow key={it.lotNumber}>
                  <TableCell>
                    <Link
                      to="/lots/$id"
                      params={{ id: it.lotNumber }}
                      className="font-mono underline underline-offset-4"
                    >
                      {it.lotNumber}
                    </Link>
                  </TableCell>
                  <TableCell>
                    <Badge variant="secondary">{lotStatusLabel(it.status)}</Badge>
                  </TableCell>
                  <TableCell>{it.manufacturingCompletedDate ?? "—"}</TableCell>
                  <TableCell className="text-right text-muted-foreground">v{it.version}</TableCell>
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
