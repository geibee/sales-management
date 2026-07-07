/**
 * 調整率と査定合計の純粋関数 (RichActionForms から抽出。issue #9 Tier2-11 / Phase 9b)。
 *
 * 調整率は画面では百分率 (許容 90〜110) で入力し、API には 1/100 した値
 * (0.9〜1.1) で送る。この変換規約は openapi.yaml の adjustmentRate 制約と
 * 対になっているため、変更するときは契約側と同時に更新すること。
 */

export const RATE_DISPLAY_MIN = 90;
export const RATE_DISPLAY_MAX = 110;
export const RATE_DISPLAY_DEFAULT = 100;
export const RATE_DISPLAY_SCALE = 100;

/** 画面の百分率 → API の倍率 (例: 105 → 1.05)。 */
export function displayToApiRate(display: number): number {
  return display / RATE_DISPLAY_SCALE;
}

/** API の倍率 → 画面の百分率 (例: 1.05 → 105)。 */
export function apiToDisplayRate(api: number): number {
  return api * RATE_DISPLAY_SCALE;
}

/** 画面入力値が許容範囲 [RATE_DISPLAY_MIN, RATE_DISPLAY_MAX] 内か。 */
export function isRateDisplayInRange(display: number): boolean {
  return Number.isFinite(display) && display >= RATE_DISPLAY_MIN && display <= RATE_DISPLAY_MAX;
}

/** 査定明細 1 行分のレート入力 (画面の百分率のまま)。exceptional は未入力なら null。 */
export interface AppraisalRateRow {
  base: number;
  period: number;
  counterparty: number;
  exceptional: number | null;
}

/**
 * 明細行から税抜査定合計を求める。例外調整率は未入力 (null) なら ×1 として扱う。
 * 非数を含む行は合計から除外する (入力途中の再計算で NaN を伝播させない)。
 * Amount は整数なので合計を丸める。
 */
export function computeEstimatedTotal(rows: AppraisalRateRow[]): number {
  let total = 0;
  for (const row of rows) {
    const base = row.base;
    const period = displayToApiRate(row.period);
    const counterparty = displayToApiRate(row.counterparty);
    const exceptional = row.exceptional === null ? 1 : displayToApiRate(row.exceptional);

    if (![base, period, counterparty, exceptional].every(Number.isFinite)) continue;
    total += base * period * counterparty * exceptional;
  }
  return Math.round(total);
}
