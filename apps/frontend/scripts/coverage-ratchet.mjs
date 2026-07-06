// カバレッジ・ラチェット — 「前回値から下げたら失敗」方式のしきい値ゲート
//
// 固定しきい値だと「最初から高い目標を置けず、置いた後は形骸化する」ため、
// coverage-baseline.json に記録した実測値を下限として使う。
//   - 現在値が baseline を EPSILON 以上下回る → 失敗 (テストなしのコード追加を検出)
//   - 現在値が baseline を上回る → 合格。RATCHET_UPDATE=1 で baseline を引き上げて
//     コミットすれば、以後はその値が新しい下限になる
//
// 入力: coverage/coverage-summary.json (vitest --coverage の json-summary reporter)
// 基準: coverage-baseline.json (コミット済み)
import { readFileSync, writeFileSync } from "node:fs";

// v8 カバレッジは実行順で僅かに揺れることがあるため、誤差はここまで許容する
const EPSILON = 0.1;
const METRICS = ["lines", "statements", "branches", "functions"];

const summaryPath = new URL("../coverage/coverage-summary.json", import.meta.url);
const baselinePath = new URL("../coverage-baseline.json", import.meta.url);

const total = JSON.parse(readFileSync(summaryPath, "utf8")).total;
const baseline = JSON.parse(readFileSync(baselinePath, "utf8"));

let failed = false;
let improved = false;

for (const metric of METRICS) {
  const current = total[metric].pct;
  const floor = baseline[metric];
  const delta = (current - floor).toFixed(2);
  if (current < floor - EPSILON) {
    console.error(`[coverage-ratchet] FAIL ${metric}: ${current}% < baseline ${floor}% (${delta}pt)`);
    failed = true;
  } else {
    console.log(`[coverage-ratchet] OK   ${metric}: ${current}% (baseline ${floor}%, ${delta}pt)`);
    if (current > floor + EPSILON) improved = true;
  }
}

if (failed) {
  console.error(
    "[coverage-ratchet] カバレッジが baseline から退行しています。" +
      "テストを追加するか、意図的な低下なら coverage-baseline.json を根拠付きで更新してください。",
  );
  process.exit(1);
}

if (improved) {
  if (process.env.RATCHET_UPDATE === "1") {
    const next = Object.fromEntries(METRICS.map((m) => [m, total[m].pct]));
    writeFileSync(baselinePath, `${JSON.stringify(next, null, 2)}\n`);
    console.log("[coverage-ratchet] baseline を現在値へ引き上げました。coverage-baseline.json をコミットしてください。");
  } else {
    console.log("[coverage-ratchet] baseline より改善しています。RATCHET_UPDATE=1 で引き上げられます。");
  }
}
