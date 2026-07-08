/**
 * Phase 2c — RichActionForms (FE-COMP-RICH-DA-* / SC-* / DV-* / RP-* / RC-* / CD-* / CR-* / COMMON-*)。
 *
 * `components/organisms/forms/rich-actions/RichActionForms.tsx` には 7 種類の form が
 * あり、観測契約を以下のように検査する:
 *   - 入力なしで submit → API 未呼出、各 invalid field にエラーが
 *     同時表示される (FieldReader が全項目をまとめて検査するため)
 *   - 調整率入力 (DirectAppraisalForm / SalesContractForm) は
 *     90〜110 を受け入れ 89 / 111 を拒否
 *   - 境界値 90 / 110 / 100 は submit body 上で 0.9 / 1.1 / 1.0 に
 *     換算される
 *   - pending 中の二重 / 三重 submit でも onSubmit は 1 回だけ走る
 *   - 未操作 field は赤くしない (blur or submit までは touched に
 *     入らない)
 *
 * 各テストは `makeSalesCase` で fixture を組み立て、対象 form を直接
 * 描画して `vi.fn()` で submit body を捕捉する。
 */
import {
  ConsignmentDesignationForm,
  ConsignmentResultForm,
  DateVersionActionForm,
  DirectAppraisalForm,
  ReservationConfirmationForm,
  ReservationPriceForm,
  SalesContractForm,
} from "@/components/organisms/forms/rich-actions/RichActionForms";
import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { deferred } from "../../../../support/deferred";
import {
  makeConsignmentSalesCase,
  makeDirectSalesCase,
  makeReservationSalesCase,
} from "../../../../support/fixtures";
import { renderWithApp } from "../../../../support/render";

function fill(label: string, value: string | number): void {
  fireEvent.change(screen.getByLabelText(label, { exact: false }), {
    target: { value: String(value) },
  });
}

function submitForm(buttonName: string | RegExp): void {
  const button = screen.getByRole("button", { name: buttonName });
  fireEvent.submit(button.closest("form")!);
}

// ---------- DirectAppraisalForm ----------

describe("DirectAppraisalForm (FE-COMP-RICH-DA-*)", () => {
  it("FE-COMP-RICH-DA-001: 必須空で submit → API 未呼出、全 invalid field にエラー", async () => {
    const onSubmit = vi.fn();
    const data = makeDirectSalesCase({ lots: ["2026-A-1"] });
    renderWithApp(
      <DirectAppraisalForm data={data} title="査定 登録" buttonLabel="登録" onSubmit={onSubmit} />,
    );
    // 必須テキストを空にする
    fireEvent.change(screen.getByLabelText("販売市場"), { target: { value: "" } });
    fireEvent.change(screen.getByLabelText("査定日"), { target: { value: "" } });
    fireEvent.change(screen.getByLabelText("納期"), { target: { value: "" } });
    submitForm("登録");
    // FieldReader が空欄を全件 alert 化する。「販売市場を入力してください」など複数 alert が出ることを確認。
    const alerts = await screen.findAllByRole("alert");
    expect(alerts.length).toBeGreaterThanOrEqual(2);
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it("FE-COMP-RICH-DA-002: 期中調整率 89 (範囲外) → field エラー、API 未呼出", async () => {
    const onSubmit = vi.fn();
    const data = makeDirectSalesCase({ lots: ["2026-A-1"] });
    renderWithApp(
      <DirectAppraisalForm data={data} title="査定" buttonLabel="登録" onSubmit={onSubmit} />,
    );
    fill("期中調整率(%)", 89);
    submitForm("登録");
    expect(await screen.findByText(/期中調整率は90以上/)).toBeInTheDocument();
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it("FE-COMP-RICH-DA-003: 期中調整率 90 / 取引先調整率 110 (境界) → API 受理、body は ÷100 換算", async () => {
    const onSubmit = vi.fn<(body: Record<string, unknown>) => Promise<void>>(async () => {});
    const data = makeDirectSalesCase({ lots: ["2026-A-1"] });
    renderWithApp(
      <DirectAppraisalForm data={data} title="査定" buttonLabel="登録" onSubmit={onSubmit} />,
    );
    fill("期中調整率(%)", 90);
    fill("取引先調整率(%)", 110);
    submitForm("登録");
    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
    const body = onSubmit.mock.calls[0]![0] as unknown as {
      lotAppraisals: Array<{
        detailAppraisals: Array<{
          periodAdjustmentRate: number;
          counterpartyAdjustmentRate: number;
        }>;
      }>;
    };
    expect(body.lotAppraisals[0]!.detailAppraisals[0]!.periodAdjustmentRate).toBeCloseTo(0.9, 5);
    expect(body.lotAppraisals[0]!.detailAppraisals[0]!.counterpartyAdjustmentRate).toBeCloseTo(
      1.1,
      5,
    );
  });

  it("FE-COMP-RICH-DA-004: 単価/調整率を変えると税抜査定合計が `Σ 基準単価 × rate ÷ 100` で即更新", async () => {
    const data = makeDirectSalesCase({ lots: ["2026-A-1", "2026-A-2"] });
    renderWithApp(
      <DirectAppraisalForm data={data} title="査定" buttonLabel="登録" onSubmit={vi.fn()} />,
    );
    // 初期は 単価1000×rate1.0×rate1.0 × 2 ロット = 2000
    expect(screen.getByText(/2,000/)).toBeInTheDocument();
    // 1ロット目の基準単価を 2000、期中rateを110% に変える → 2000 * 1.1 * 1.0 = 2200、+ 2ロット目 1000 = 3200
    const baseUnit = screen.getAllByLabelText("基準単価");
    fireEvent.change(baseUnit[0]!, { target: { value: "2000" } });
    const periodRate = screen.getAllByLabelText("期中調整率(%)");
    fireEvent.change(periodRate[0]!, { target: { value: "110" } });
    await waitFor(() => expect(screen.getByText(/3,200/)).toBeInTheDocument());
  });

  it("FE-COMP-RICH-DA-005: 「変更する」→ 承認 → total input enabled、承認者は read-only `営業部長（システム既定）`", async () => {
    const data = makeDirectSalesCase({ lots: ["2026-A-1"] });
    renderWithApp(
      <DirectAppraisalForm data={data} title="査定" buttonLabel="登録" onSubmit={vi.fn()} />,
    );
    fireEvent.click(screen.getByRole("button", { name: "変更する" }));
    // モーダルが開く
    expect(await screen.findByRole("dialog")).toBeInTheDocument();
    // 承認者は readOnly
    const approver = screen.getByLabelText("承認者") as HTMLInputElement;
    expect(approver.value).toBe("営業部長（システム既定）");
    expect(approver).toHaveAttribute("readonly");
    // 未チェックでは確定 disabled
    const enable = screen.getByRole("button", { name: "直接入力を有効化" });
    expect(enable).toBeDisabled();
    fireEvent.click(screen.getByRole("checkbox"));
    expect(enable).not.toBeDisabled();
    fireEvent.click(enable);
    // total が編集可能な input に変わる
    const total = (await screen.findByLabelText("税抜査定合計")) as HTMLInputElement;
    expect(total.tagName).toBe("INPUT");
    expect(total).not.toBeDisabled();
  });

  it("FE-COMP-RICH-DA-006: 「変更する」→ キャンセル → total は自動計算のまま (hidden input)", async () => {
    const data = makeDirectSalesCase({ lots: ["2026-A-1"] });
    renderWithApp(
      <DirectAppraisalForm data={data} title="査定" buttonLabel="登録" onSubmit={vi.fn()} />,
    );
    fireEvent.click(screen.getByRole("button", { name: "変更する" }));
    await screen.findByRole("dialog");
    fireEvent.click(screen.getByRole("button", { name: "キャンセル" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    // 自動計算ラベルが残る
    expect(screen.getByText(/1,000/)).toBeInTheDocument();
  });
});

// ---------- SalesContractForm ----------

describe("SalesContractForm (FE-COMP-RICH-SC-*)", () => {
  it("FE-COMP-RICH-SC-001: 必須空で submit → API 未呼出 (顧客番号等を空に戻す)", async () => {
    const onSubmit = vi.fn();
    const data = makeDirectSalesCase();
    renderWithApp(
      <SalesContractForm data={data} title="契約" buttonLabel="登録" onSubmit={onSubmit} />,
    );
    fireEvent.change(screen.getByLabelText("顧客番号"), { target: { value: "" } });
    fireEvent.change(screen.getByLabelText("品目"), { target: { value: "" } });
    fireEvent.change(screen.getByLabelText("納入方法"), { target: { value: "" } });
    submitForm("登録");
    const alerts = await screen.findAllByRole("alert");
    expect(alerts.length).toBeGreaterThanOrEqual(1);
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it("FE-COMP-RICH-SC-002: 契約調整率 89 (範囲外) → field エラー、API 未呼出", async () => {
    const onSubmit = vi.fn();
    const data = makeDirectSalesCase();
    renderWithApp(
      <SalesContractForm data={data} title="契約" buttonLabel="登録" onSubmit={onSubmit} />,
    );
    fill("契約調整率(%)", 89);
    submitForm("登録");
    expect(await screen.findByText(/契約調整率は90以上/)).toBeInTheDocument();
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it("FE-COMP-RICH-SC-003: 契約調整率 100 → API 受理、body は ÷100 換算で 1.0", async () => {
    const onSubmit = vi.fn<(body: Record<string, unknown>) => Promise<void>>(async () => {});
    const data = makeDirectSalesCase();
    renderWithApp(
      <SalesContractForm data={data} title="契約" buttonLabel="登録" onSubmit={onSubmit} />,
    );
    fill("契約調整率(%)", 100);
    submitForm("登録");
    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
    expect(onSubmit.mock.calls[0]![0].contractAdjustmentRate).toBeCloseTo(1.0, 5);
  });
});

// ---------- DateVersionActionForm ----------

describe("DateVersionActionForm (FE-COMP-RICH-DV-*)", () => {
  it("FE-COMP-RICH-DV-001: 必須 date 空 → API 未呼出、field 直下にエラー", async () => {
    const onSubmit = vi.fn();
    renderWithApp(
      <DateVersionActionForm
        title="出荷指示"
        buttonLabel="登録"
        dateLabel="出荷予定日"
        defaultDate=""
        version={1}
        onSubmit={onSubmit}
      />,
    );
    submitForm("登録");
    expect(await screen.findByText("出荷予定日を入力してください")).toBeInTheDocument();
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it("FE-COMP-RICH-DV-002: 有効 date + version → submit body に date と version が含まれる", async () => {
    const onSubmit = vi.fn<(body: Record<string, unknown>) => Promise<void>>(async () => {});
    renderWithApp(
      <DateVersionActionForm
        title="出荷指示"
        buttonLabel="登録"
        dateLabel="出荷予定日"
        defaultDate="2026-05-01"
        version={3}
        onSubmit={onSubmit}
      />,
    );
    submitForm("登録");
    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
    expect(onSubmit.mock.calls[0]![0]).toMatchObject({ date: "2026-05-01", version: 3 });
  });
});

// ---------- ReservationPriceForm ----------

describe("ReservationPriceForm (FE-COMP-RICH-RP-*)", () => {
  it("FE-COMP-RICH-RP-001: 予約金額が空 → field エラー、API 未呼出", async () => {
    const onSubmit = vi.fn();
    const data = makeReservationSalesCase();
    renderWithApp(<ReservationPriceForm data={data} onSubmit={onSubmit} />);
    fireEvent.change(screen.getByLabelText("予約金額"), { target: { value: "" } });
    submitForm("登録");
    expect(await screen.findByText("予約金額を入力してください")).toBeInTheDocument();
    expect(onSubmit).not.toHaveBeenCalled();
  });
});

// ---------- ReservationConfirmationForm ----------

describe("ReservationConfirmationForm (FE-COMP-RICH-RC-*)", () => {
  it("FE-COMP-RICH-RC-001: 確定金額空 → field エラー、API 未呼出", async () => {
    const onSubmit = vi.fn();
    const data = makeReservationSalesCase();
    renderWithApp(<ReservationConfirmationForm data={data} onSubmit={onSubmit} />);
    fireEvent.change(screen.getByLabelText("確定金額"), { target: { value: "" } });
    submitForm("確定");
    expect(await screen.findByText("確定金額を入力してください")).toBeInTheDocument();
    expect(onSubmit).not.toHaveBeenCalled();
  });
});

// ---------- ConsignmentDesignationForm ----------

describe("ConsignmentDesignationForm (FE-COMP-RICH-CD-*)", () => {
  it("FE-COMP-RICH-CD-001: 委託先名空 → field エラー、API 未呼出", async () => {
    const onSubmit = vi.fn();
    const data = makeConsignmentSalesCase();
    renderWithApp(<ConsignmentDesignationForm data={data} onSubmit={onSubmit} />);
    fireEvent.change(screen.getByLabelText("委託先名"), { target: { value: "" } });
    submitForm("登録");
    expect(await screen.findByText("委託先名を入力してください")).toBeInTheDocument();
    expect(onSubmit).not.toHaveBeenCalled();
  });
});

// ---------- ConsignmentResultForm ----------

describe("ConsignmentResultForm (FE-COMP-RICH-CR-*)", () => {
  it("FE-COMP-RICH-CR-001: 結果金額空 → field エラー、API 未呼出", async () => {
    const onSubmit = vi.fn();
    const data = makeConsignmentSalesCase();
    renderWithApp(<ConsignmentResultForm data={data} onSubmit={onSubmit} />);
    fireEvent.change(screen.getByLabelText("結果金額"), { target: { value: "" } });
    submitForm("登録");
    expect(await screen.findByText("結果金額を入力してください")).toBeInTheDocument();
    expect(onSubmit).not.toHaveBeenCalled();
  });
});

// ---------- COMMON ----------

describe("RichActionForms 共通 (FE-COMP-RICH-COMMON-*)", () => {
  it("FE-COMP-RICH-COMMON-001: 二重 submit (有効入力) でも onSubmit 呼出は 1 回", async () => {
    const d = deferred<void>();
    const onSubmit = vi.fn(() => d.promise);
    renderWithApp(
      <DateVersionActionForm
        title="出荷指示"
        buttonLabel="登録"
        dateLabel="出荷予定日"
        defaultDate="2026-05-01"
        version={1}
        onSubmit={onSubmit}
      />,
    );
    fireEvent.click(screen.getByRole("button", { name: "登録" }));
    expect(await screen.findByRole("button", { name: "実行中…" })).toBeDisabled();
    // disabled button への click は no-op
    fireEvent.click(screen.getByRole("button", { name: "実行中…" }));
    fireEvent.click(screen.getByRole("button", { name: "実行中…" }));
    d.resolve();
    await waitFor(() => expect(screen.queryByRole("button", { name: "実行中…" })).toBeNull());
    expect(onSubmit).toHaveBeenCalledTimes(1);
  });

  // FE-A11Y-RICH-001: 7 form 全部で「全 label ↔ input の紐付け」を検査する。
  // ラベル文字列を列挙せず form 内の label / 可視 input を全数機械列挙するため、
  // form に field を追加しても検査から漏れない (紐付け忘れは即赤になる)。
  const allForms: Array<[name: string, doRender: () => void]> = [
    [
      "DirectAppraisalForm",
      () =>
        renderWithApp(
          <DirectAppraisalForm
            data={makeDirectSalesCase({ lots: ["2026-A-1"] })}
            title="査定"
            buttonLabel="登録"
            onSubmit={vi.fn()}
          />,
        ),
    ],
    [
      "SalesContractForm",
      () =>
        renderWithApp(
          <SalesContractForm
            data={makeDirectSalesCase({ lots: ["2026-A-1"] })}
            title="契約"
            buttonLabel="登録"
            onSubmit={vi.fn()}
          />,
        ),
    ],
    [
      "DateVersionActionForm",
      () =>
        renderWithApp(
          <DateVersionActionForm
            title="出荷指示"
            buttonLabel="登録"
            dateLabel="出荷予定日"
            defaultDate="2026-05-01"
            version={1}
            onSubmit={vi.fn()}
          />,
        ),
    ],
    [
      "ReservationPriceForm",
      () =>
        renderWithApp(
          <ReservationPriceForm data={makeReservationSalesCase()} onSubmit={vi.fn()} />,
        ),
    ],
    [
      "ReservationConfirmationForm",
      () =>
        renderWithApp(
          <ReservationConfirmationForm data={makeReservationSalesCase()} onSubmit={vi.fn()} />,
        ),
    ],
    [
      "ConsignmentDesignationForm",
      () =>
        renderWithApp(
          <ConsignmentDesignationForm data={makeConsignmentSalesCase()} onSubmit={vi.fn()} />,
        ),
    ],
    [
      "ConsignmentResultForm",
      () =>
        renderWithApp(
          <ConsignmentResultForm data={makeConsignmentSalesCase()} onSubmit={vi.fn()} />,
        ),
    ],
  ];

  it.each(allForms)("FE-A11Y-RICH-001: %s — 全 input が label と紐付く", (_name, doRender) => {
    doRender();
    const formEl = document.querySelector("form");
    expect(formEl).not.toBeNull();

    // 可視 input 全数: アクセシブルネーム (label 由来) を必ず持つ
    const controls = Array.from(
      formEl!.querySelectorAll<HTMLElement>("input:not([type='hidden']), select, textarea"),
    ).filter((el) => el.closest("[aria-hidden='true']") === null);
    expect(controls.length).toBeGreaterThan(0);
    for (const el of controls) {
      expect(el, `${el.getAttribute("name") ?? el.tagName} に label がない`).toHaveAccessibleName();
    }

    // label 全数: htmlFor が実在の control を指す (孤立 label の検出)
    const labels = Array.from(formEl!.querySelectorAll<HTMLLabelElement>("label"));
    expect(labels.length).toBeGreaterThan(0);
    for (const label of labels) {
      if (!label.htmlFor) continue;
      const target = document.getElementById(label.htmlFor);
      expect(
        target,
        `label "${label.textContent}" の htmlFor="${label.htmlFor}" 先が存在しない: ${label.outerHTML}`,
      ).not.toBeNull();
    }
  });

  it("FE-COMP-RICH-COMMON-002: mount 直後 (blur なし) はエラー非表示", () => {
    const data = makeDirectSalesCase();
    renderWithApp(
      <DirectAppraisalForm data={data} title="査定" buttonLabel="登録" onSubmit={vi.fn()} />,
    );
    // role="alert" は FieldError が出した時にだけ生える
    expect(screen.queryByRole("alert")).toBeNull();
    // aria-invalid="true" な input も存在しない
    const formEl = screen.getByRole("button", { name: "登録" }).closest("form")!;
    const invalid = within(formEl)
      .queryAllByLabelText(/./)
      .filter((el) => el.getAttribute("aria-invalid") === "true");
    expect(invalid).toHaveLength(0);
  });
});
