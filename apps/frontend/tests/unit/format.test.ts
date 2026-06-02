/**
 * `src/lib/format.ts` のロジック単体テスト（計画 P1-1）。
 *
 * 本丸は `lotActionEnabled` — バックエンド状態機械 (`LotRoutes.fs`) の写し。
 * ここがドリフトするとロット詳細のアクションボタン活性/非活性を誤るため、
 * (action × status) の全真理値表を固定して「ずれたら赤くなる」状態にする。
 * あわせて caseType 分岐を持つ `caseStatusLabel` / `caseStatusTone` /
 * `lotStatusTone` を検証する。
 */
import {
  type LotAction,
  caseStatusLabel,
  caseStatusTone,
  caseTypeLabel,
  codeName,
  formatAmount,
  formatQuantity,
  lotActionEnabled,
  lotStatusLabel,
  lotStatusTone,
} from "@/lib/format";
import { describe, expect, it } from "vitest";

/** ドメインに存在する全ロット状態。 */
const LOT_STATUSES = [
  "manufacturing",
  "manufactured",
  "shipping_instructed",
  "shipped",
  "conversion_instructed",
] as const;

/**
 * 各アクションを活性化する「唯一の」状態。`LotRoutes.fs` の状態遷移制約と
 * 1:1 対応する。ここを真とし、他状態は全て非活性であることを表で確認する。
 */
const ENABLING_STATUS: Record<LotAction, (typeof LOT_STATUSES)[number]> = {
  "complete-manufacturing": "manufacturing",
  "cancel-manufacturing-completion": "manufactured",
  "instruct-shipping": "manufactured",
  "complete-shipping": "shipping_instructed",
  "instruct-item-conversion": "manufactured",
  "cancel-item-conversion-instruction": "conversion_instructed",
};

describe("lotActionEnabled (状態機械の写し)", () => {
  const actions = Object.keys(ENABLING_STATUS) as LotAction[];

  for (const action of actions) {
    for (const status of LOT_STATUSES) {
      const expected = ENABLING_STATUS[action] === status;
      it(`${action} × ${status} → ${expected}`, () => {
        expect(lotActionEnabled(action, status)).toBe(expected);
      });
    }
  }

  it("null / undefined / 未知状態は常に非活性", () => {
    for (const action of actions) {
      expect(lotActionEnabled(action, null)).toBe(false);
      expect(lotActionEnabled(action, undefined)).toBe(false);
      expect(lotActionEnabled(action, "bogus_status")).toBe(false);
    }
  });

  it("未知アクションは非活性 (default 分岐)", () => {
    expect(lotActionEnabled("not-an-action" as LotAction, "manufactured")).toBe(false);
  });
});

describe("lotStatusLabel / lotStatusTone", () => {
  it("既知状態は日本語ラベルとトーンを返す", () => {
    expect(lotStatusLabel("manufacturing")).toBe("製造中");
    expect(lotStatusLabel("manufactured")).toBe("製造完了");
    expect(lotStatusLabel("shipping_instructed")).toBe("出荷指示済");
    expect(lotStatusLabel("shipped")).toBe("出荷完了");
    expect(lotStatusLabel("conversion_instructed")).toBe("変換指示済");

    expect(lotStatusTone("manufacturing")).toBe("info");
    expect(lotStatusTone("manufactured")).toBe("ok");
    expect(lotStatusTone("shipping_instructed")).toBe("accent");
    expect(lotStatusTone("shipped")).toBe("neutral");
    expect(lotStatusTone("conversion_instructed")).toBe("warn");
  });

  it("null は (unknown) / neutral、未知コードは素通し / neutral", () => {
    expect(lotStatusLabel(null)).toBe("(unknown)");
    expect(lotStatusLabel(undefined)).toBe("(unknown)");
    expect(lotStatusLabel("weird")).toBe("weird");
    expect(lotStatusTone(null)).toBe("neutral");
    expect(lotStatusTone("weird")).toBe("neutral");
  });
});

describe("caseStatusLabel (caseType 分岐)", () => {
  it("direct (既定): 販売案件のラベル", () => {
    expect(caseStatusLabel("direct", "before_appraisal")).toBe("査定前");
    expect(caseStatusLabel("direct", "appraised")).toBe("査定済");
    expect(caseStatusLabel("direct", "contracted")).toBe("契約済");
    expect(caseStatusLabel("direct", "shipping_instructed")).toBe("出荷指示済");
    expect(caseStatusLabel("direct", "shipping_completed")).toBe("出荷完了");
  });

  it("reservation: 予約のラベル", () => {
    expect(caseStatusLabel("reservation", "before_reservation")).toBe("査定前");
    expect(caseStatusLabel("reservation", "reserved")).toBe("予約済");
    expect(caseStatusLabel("reservation", "reservation_confirmed")).toBe("予約確定済");
    expect(caseStatusLabel("reservation", "reservation_delivered")).toBe("引渡済");
  });

  it("consignment: 委託のラベル", () => {
    expect(caseStatusLabel("consignment", "before_consignment")).toBe("委託前");
    expect(caseStatusLabel("consignment", "consignment_designated")).toBe("委託指定済");
    expect(caseStatusLabel("consignment", "consignment_result_entered")).toBe("結果登録済");
  });

  it("未知 caseType は direct (販売案件) マップへフォールバック", () => {
    expect(caseStatusLabel(null, "appraised")).toBe("査定済");
    expect(caseStatusLabel("unknown_type", "contracted")).toBe("契約済");
  });

  it("status が null は (unknown)、マップ外は素通し", () => {
    expect(caseStatusLabel("direct", null)).toBe("(unknown)");
    expect(caseStatusLabel("reservation", "weird")).toBe("weird");
  });
});

describe("caseStatusTone (全 caseType を 1 マップで解決)", () => {
  it("direct / reservation / consignment 各状態のトーン", () => {
    expect(caseStatusTone("before_appraisal")).toBe("neutral");
    expect(caseStatusTone("appraised")).toBe("info");
    expect(caseStatusTone("contracted")).toBe("accent");
    expect(caseStatusTone("shipping_instructed")).toBe("warn");
    expect(caseStatusTone("shipping_completed")).toBe("ok");

    expect(caseStatusTone("reserved")).toBe("info");
    expect(caseStatusTone("reservation_confirmed")).toBe("accent");
    expect(caseStatusTone("reservation_delivered")).toBe("ok");

    expect(caseStatusTone("consignment_designated")).toBe("info");
    expect(caseStatusTone("consignment_result_entered")).toBe("ok");
  });

  it("null / 未知は neutral", () => {
    expect(caseStatusTone(null)).toBe("neutral");
    expect(caseStatusTone("weird")).toBe("neutral");
  });
});

describe("caseTypeLabel", () => {
  it("既知種別は日本語、null は (unknown)、未知は素通し", () => {
    expect(caseTypeLabel("direct")).toBe("直接販売");
    expect(caseTypeLabel("reservation")).toBe("予約");
    expect(caseTypeLabel("consignment")).toBe("委託");
    expect(caseTypeLabel(null)).toBe("(unknown)");
    expect(caseTypeLabel("weird")).toBe("weird");
  });
});

describe("数値・コードのフォーマッタ", () => {
  it("formatAmount: 整数を ja-JP 桁区切り", () => {
    expect(formatAmount(1234567)).toBe("1,234,567");
    expect(formatAmount(0)).toBe("0");
  });

  it("formatQuantity: 小数最大 3 桁で桁区切り", () => {
    expect(formatQuantity(1234.5)).toBe("1,234.5");
    expect(formatQuantity(1.23456)).toBe("1.235");
  });

  it("codeName: name 有りは「名称 (コード)」、無しはコードのみ", () => {
    expect(codeName({ code: 7, name: "金" })).toBe("金 (7)");
    expect(codeName({ code: 7, name: null })).toBe("7");
    expect(codeName({ code: 7 })).toBe("7");
  });
});
