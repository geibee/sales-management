/**
 * scripts/coverage-ratchet.mjs (frontend カバレッジラチェット) の単体テスト
 * (issue #9 Tier2-17)。
 *
 * ラチェット自体が壊れると全 PR が素通り (fail-open) になるため、
 * 子プロセスとして実行して exit code と baseline 書き換えを固定する。
 */
import { execFileSync } from "node:child_process";
import { mkdtempSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { describe, expect, it } from "vitest";

const SCRIPT = join(__dirname, "../../../scripts/coverage-ratchet.mjs");
const METRICS = ["lines", "statements", "branches", "functions"] as const;

function makeSummary(pct: Record<string, number>): string {
  const total = Object.fromEntries(METRICS.map((m) => [m, { pct: pct[m] }]));
  return JSON.stringify({ total });
}

function run(
  current: Record<string, number>,
  baseline: Record<string, number>,
  env: Record<string, string> = {},
): { status: number; baselinePath: string } {
  const dir = mkdtempSync(join(tmpdir(), "ratchet-"));
  const summaryPath = join(dir, "coverage-summary.json");
  const baselinePath = join(dir, "coverage-baseline.json");
  writeFileSync(summaryPath, makeSummary(current));
  writeFileSync(baselinePath, JSON.stringify(baseline));
  try {
    execFileSync(process.execPath, [SCRIPT], {
      env: {
        ...process.env,
        RATCHET_UPDATE: "",
        COVERAGE_SUMMARY_PATH: summaryPath,
        COVERAGE_BASELINE_PATH: baselinePath,
        ...env,
      },
      stdio: "pipe",
    });
    return { status: 0, baselinePath };
  } catch (e) {
    return { status: (e as { status: number }).status, baselinePath };
  }
}

const flat = (pct: number): Record<string, number> =>
  Object.fromEntries(METRICS.map((m) => [m, pct]));

describe("coverage-ratchet.mjs", () => {
  it("baseline と同値なら合格する", () => {
    expect(run(flat(80), flat(80)).status).toBe(0);
  });

  it("EPSILON (0.1pt) を超えて下回ると失敗する", () => {
    expect(run(flat(79), flat(80)).status).toBe(1);
  });

  it("EPSILON 以内の揺れは許容する", () => {
    expect(run(flat(79.95), flat(80)).status).toBe(0);
  });

  it("1 メトリクスだけの退行でも失敗する", () => {
    expect(run({ ...flat(80), branches: 70 }, flat(80)).status).toBe(1);
  });

  it("改善時は既定で baseline を書き換えない", () => {
    const { status, baselinePath } = run(flat(90), flat(80));
    expect(status).toBe(0);
    expect(JSON.parse(readFileSync(baselinePath, "utf8"))).toEqual(flat(80));
  });

  it("RATCHET_UPDATE=1 で baseline を現在値へ引き上げる", () => {
    const { status, baselinePath } = run(flat(90), flat(80), { RATCHET_UPDATE: "1" });
    expect(status).toBe(0);
    expect(JSON.parse(readFileSync(baselinePath, "utf8"))).toEqual(flat(90));
  });

  it("summary が無ければ失敗する (fail-closed)", () => {
    const dir = mkdtempSync(join(tmpdir(), "ratchet-"));
    const baselinePath = join(dir, "coverage-baseline.json");
    writeFileSync(baselinePath, JSON.stringify(flat(80)));
    expect(() =>
      execFileSync(process.execPath, [SCRIPT], {
        env: {
          ...process.env,
          COVERAGE_SUMMARY_PATH: join(dir, "missing.json"),
          COVERAGE_BASELINE_PATH: baselinePath,
        },
        stdio: "pipe",
      }),
    ).toThrow();
  });
});
