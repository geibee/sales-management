/**
 * fast-check 実行パラメータの共通設定 (issue #9 Tier2-11 / Phase 9a)。
 * 既定 100 runs。nightly は FE_PBT_RUNS=1000 で厚めに回す。
 */
export const pbtNumRuns = Number(process.env.FE_PBT_RUNS ?? 100);

export const pbtOpts = { numRuns: pbtNumRuns } as const;

/** PBT ファイル用の testTimeout (shrinking が時間を食うため通常より長め)。 */
export const pbtTimeout = 15_000;
