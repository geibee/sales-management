import {
  DCard,
  DCardBody,
  DCardHeader,
  DLRow,
  DesignPageHeader,
  Pill,
} from "@/components/design/primitives";
import { useExternalPriceCheck } from "@/hooks/use-external-pricing";
import { ApiError, describeApiError } from "@/lib/api-client";
import { Search } from "lucide-react";
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
    <div className="page">
      <DesignPageHeader
        eyebrow="外部連携"
        title="外部価格チェック"
        subtitle="外部価格 API 経由で参考価格を取得します。レート制限・サーキットブレーカーの対象です。"
      />

      <div className="split-2">
        <DCard>
          <DCardHeader title="クエリ" icon={<Search className="ico" size={15} />} />
          <DCardBody>
            <div className="field">
              <div className="label">
                <label htmlFor="lotId">ロット番号</label>
                <span className="label-hint">必須</span>
              </div>
              <input
                id="lotId"
                name="lotId"
                className="input mono"
                placeholder="2026-A-001"
                value={lotIdInput}
                onChange={(e) => setLotIdInput(e.target.value)}
              />
              <div className="field-help">
                バックエンドは <span className="mono">lotId</span> 必須。空欄の場合 400 を返します。
              </div>
            </div>

            <div className="row gap-2 mt-3">
              <button
                type="button"
                className="btn btn-primary"
                onClick={handleFetch}
                disabled={!canSubmit}
              >
                {!isLoading && <Search className="ico" />}
                {isLoading ? "取得中…" : "取得"}
              </button>
              <button type="button" className="btn btn-ghost" onClick={() => setLotIdInput("")}>
                クリア
              </button>
            </div>

            <hr className="sep" />

            <div className="row" style={{ justifyContent: "space-between" }}>
              <span className="text-sm muted">サーキット状態</span>
              {isCircuitOpen ? (
                <Pill tone="danger" dot>
                  OPEN · 遮断中
                </Pill>
              ) : (
                <Pill tone="ok" dot>
                  CLOSED · 正常
                </Pill>
              )}
            </div>
          </DCardBody>
        </DCard>

        <div className="col gap-4">
          {error && (
            <div className="card-d" style={{ borderColor: "oklch(0.85 0.06 25)", padding: 18 }}>
              <p style={{ color: "var(--danger)", fontWeight: 600, fontSize: 13, margin: 0 }}>
                {isCircuitOpen
                  ? "サーキットが OPEN しています。しばらく経ってから再試行してください。"
                  : isUpstream
                    ? "上流の価格 API がエラーを返しました。"
                    : isBadRequest
                      ? "ロット番号の形式が不正です。"
                      : "取得に失敗しました。"}
              </p>
              <p className="text-xs muted mt-2" style={{ margin: "8px 0 0" }}>
                {describeApiError(error)}
              </p>
            </div>
          )}

          {data && !error && (
            <>
              <div className="price-result">
                <div className="col gap-2">
                  <div className="row gap-2">
                    <Pill tone="accent" mono>
                      {submittedLotId}
                    </Pill>
                  </div>
                  <div className="price-big tnum mt-2">¥{data.basePrice}</div>
                </div>
              </div>

              <DCard>
                <DCardHeader title="取得結果" />
                <DCardBody>
                  <dl className="dl dl-tight">
                    <DLRow label="基準単価">
                      <span className="mono tnum">{data.basePrice}</span>
                    </DLRow>
                    <DLRow label="調整率">
                      <span className="mono tnum">{data.adjustmentRate ?? "(未設定)"}</span>
                    </DLRow>
                    <DLRow label="ソース">{data.source}</DLRow>
                  </dl>
                </DCardBody>
              </DCard>
            </>
          )}

          {!data && !error && (
            <DCard>
              <DCardBody>
                <p className="muted text-sm" style={{ margin: 0 }}>
                  ロット番号を入力して「取得」を押すと、外部マーケットの参考価格を表示します。
                </p>
              </DCardBody>
            </DCard>
          )}
        </div>
      </div>
    </div>
  );
}
