import { Badge } from "@/components/atoms/badge";
import { Button } from "@/components/atoms/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/atoms/dialog";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/atoms/table";
import { useAvailableLots } from "@/hooks/use-available-lots";
import { describeApiError } from "@/lib/api-client";
import { lotStatusLabel } from "@/lib/format";
import { useEffect, useRef, useState } from "react";

/**
 * 製造完了かつ未割当のロットを一覧表示し、チェックボックスで選択させるモーダル。
 * 販売案件の新規作成・ロット修正の双方で共有する。
 * excludeCase を渡すと、その案件に現在割り当て済みのロットも候補に含まれる（修正時に自案件分を残す）。
 */
export function LotSelectDialog({
  open,
  onOpenChange,
  value,
  onConfirm,
  excludeCase,
  title = "ロットを選択",
  confirmLabel = "確定",
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  value: string[];
  onConfirm: (lots: string[]) => void;
  excludeCase?: string;
  title?: string;
  confirmLabel?: string;
}) {
  const { data, error, isLoading } = useAvailableLots({ excludeCase, enabled: open });
  const items = data?.items ?? [];

  const [selected, setSelected] = useState<Set<string>>(() => new Set(value));
  const wasOpen = useRef(false);

  // モーダルが開いた瞬間だけ、渡された value で選択状態を初期化する。
  useEffect(() => {
    if (open && !wasOpen.current) setSelected(new Set(value));
    wasOpen.current = open;
  });

  const toggle = (lotNumber: string) => {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(lotNumber)) next.delete(lotNumber);
      else next.add(lotNumber);
      return next;
    });
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          <DialogDescription>
            製造完了かつ他の販売案件に割り当てられていないロットを選択できます。
          </DialogDescription>
        </DialogHeader>

        {error && <p className="text-destructive text-sm">エラー: {describeApiError(error)}</p>}

        <div className="max-h-[50vh] overflow-y-auto rounded-md border">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead className="w-10" />
                <TableHead>ロット番号</TableHead>
                <TableHead>状態</TableHead>
                <TableHead>製造完了日</TableHead>
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
                    選択可能なロットがありません
                  </TableCell>
                </TableRow>
              ) : (
                items.map((it) => (
                  <TableRow
                    key={it.lotNumber}
                    className="cursor-pointer"
                    onClick={() => toggle(it.lotNumber)}
                  >
                    <TableCell>
                      <input
                        type="checkbox"
                        className="size-4"
                        checked={selected.has(it.lotNumber)}
                        onChange={() => toggle(it.lotNumber)}
                        onClick={(e) => e.stopPropagation()}
                        aria-label={`ロット ${it.lotNumber} を選択`}
                      />
                    </TableCell>
                    <TableCell className="font-mono">{it.lotNumber}</TableCell>
                    <TableCell>
                      <Badge variant="secondary">{lotStatusLabel(it.status)}</Badge>
                    </TableCell>
                    <TableCell>{it.manufacturingCompletedDate ?? "—"}</TableCell>
                  </TableRow>
                ))
              )}
            </TableBody>
          </Table>
        </div>

        <DialogFooter className="sm:items-center sm:justify-between">
          <span className="text-muted-foreground text-sm">{selected.size} 件選択中</span>
          <div className="flex gap-2">
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              キャンセル
            </Button>
            <Button
              type="button"
              disabled={selected.size === 0}
              onClick={() => {
                onConfirm([...selected]);
                onOpenChange(false);
              }}
            >
              {confirmLabel}
            </Button>
          </div>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
