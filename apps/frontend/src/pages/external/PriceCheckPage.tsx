import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { useExternalPriceCheck } from "@/hooks/use-external-pricing";
import { ApiError, describeApiError } from "@/lib/api-client";
import { useState } from "react";

export function PriceCheckPage() {
  const [lotIdInput, setLotIdInput] = useState("");
  const [submittedLotId, setSubmittedLotId] = useState<string | null>(null);
  const { data, error, isLoading, mutate } = useExternalPriceCheck(submittedLotId);

  const trimmed = lotIdInput.trim();
  const canSubmit = trimmed.length > 0 && !isLoading;

  const handleFetch = () => {
    if (!canSubmit) return;
    if (submittedLotId === trimmed) {
      mutate();
    } else {
      setSubmittedLotId(trimmed);
    }
  };

  const status = error instanceof ApiError ? error.status : null;
  const isCircuitOpen = status === 503;
  const isUpstream = status === 502;
  const isBadRequest = status === 400;

  return (
    <div className="space-y-6">
      <h1 className="font-semibold text-2xl">外部価格チェック</h1>
      <Card>
        <CardHeader>
          <CardTitle>価格を取得</CardTitle>
        </CardHeader>
        <CardContent className="space-y-4">
          <p className="text-muted-foreground text-sm">
            外部価格 API 経由で参考価格を取得します。レート制限・サーキットブレーカーの対象です。
          </p>
          <div className="space-y-1">
            <Label htmlFor="lotId">ロット番号</Label>
            <Input
              id="lotId"
              name="lotId"
              placeholder="2026-A-001"
              value={lotIdInput}
              onChange={(e) => setLotIdInput(e.target.value)}
            />
          </div>
          <div className="flex gap-2">
            <Button type="button" onClick={handleFetch} disabled={!canSubmit}>
              {isLoading ? "取得中…" : "取得"}
            </Button>
          </div>

          {error && (
            <div className="space-y-1 rounded-md border border-destructive/50 bg-destructive/5 p-3">
              <p className="font-medium text-destructive text-sm">
                {isCircuitOpen
                  ? "サーキットが OPEN しています。しばらく経ってから再試行してください。"
                  : isUpstream
                    ? "上流の価格 API がエラーを返しました。"
                    : isBadRequest
                      ? "ロット番号の形式が不正です。"
                      : "取得に失敗しました。"}
              </p>
              <p className="text-muted-foreground text-xs">{describeApiError(error)}</p>
            </div>
          )}

          {data && !error && (
            <div className="rounded-md border p-3">
              <p className="font-medium text-sm">取得結果</p>
              <dl className="mt-2 grid grid-cols-[8rem_1fr] gap-1 text-sm">
                <dt className="text-muted-foreground">基準単価</dt>
                <dd>{data.basePrice}</dd>
                <dt className="text-muted-foreground">調整率</dt>
                <dd>{data.adjustmentRate ?? "(未設定)"}</dd>
                <dt className="text-muted-foreground">ソース</dt>
                <dd>{data.source}</dd>
              </dl>
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
