import { DirectAppraisalForm } from "@/components/organisms/forms/rich-actions/RichActionForms";
/**
 * Phase 2g — 共通 Validation 表示ポリシー (FE-VAL-POLICY-001..007)。
 *
 * `docs/frontend-component-page-test-plan.md` の form 横断ポリシー:
 *   001  ブラウザ標準 popup を使わない → form は `noValidate`
 *   002  エラーは該当 field 直下に出る
 *   003  違反している全項目を同時に表示する
 *   004  未操作 field は赤くしない (rhf `mode:"onTouched"` /
 *                                    RichAction の touched 集合)
 *   005  修正後は field のエラーを即消す
 *   006  submit 時は全 invalid に aria-invalid=true、API 未呼出
 *   007  ロット ID 入力は全件列挙する
 *
 * 代表 form 3 種で検査:
 *   - LotCreatePage (rhf + zod)
 *   - SalesCaseCreatePage (rhf + zod、ロット superRefine で 007)
 *   - DirectAppraisalForm (RichActionForms / FieldReader)
 */
import { LotCreatePage } from "@/pages/lots/LotCreatePage";
import { fireEvent, screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import { makeCodeMasters, makeSalesCase } from "../support/fixtures";
import { renderWithApp, renderWithRouter } from "../support/render";
import { requestsFor, server } from "../support/server";

function authDisabled(): void {
  server.use(
    http.get("/api/auth/config", () => HttpResponse.json({ enabled: false })),
    http.get("/api/code-masters", () => HttpResponse.json(makeCodeMasters())),
  );
}

describe("Validation 表示ポリシー (FE-VAL-POLICY-*)", () => {
  it("FE-VAL-POLICY-001: form は noValidate (ブラウザ標準 popup 無効)", async () => {
    authDisabled();
    renderWithRouter(<LotCreatePage />);
    const button = await screen.findByRole("button", { name: "作成" });
    const form = button.closest("form") as HTMLFormElement;
    expect(form).toHaveAttribute("noValidate");
  });

  it("FE-VAL-POLICY-002: エラーは該当 field 直下に出る (LotCreatePage `年度`)", async () => {
    authDisabled();
    renderWithRouter(<LotCreatePage />);
    const year = (await screen.findByLabelText("年度")) as HTMLInputElement;
    fireEvent.change(year, { target: { value: "" } });
    fireEvent.click(screen.getByRole("button", { name: "作成" }));
    // input が aria-invalid になるまで待つ
    await waitFor(() => expect(year).toHaveAttribute("aria-invalid", "true"));
    // 同 wrapper 内に role="alert" のエラー要素が居ることを確認
    // (メッセージ本文は zod schema 都合で揺れ得るので含有チェックに留める)
    const wrapper = year.closest("div.space-y-1") as HTMLElement;
    expect(wrapper).not.toBeNull();
    const alert = wrapper.querySelector('[role="alert"]');
    expect(alert).not.toBeNull();
    expect(alert?.textContent).toMatch(/年度/);
  });

  it("FE-VAL-POLICY-003: 違反全項目が同時に表示される (年度・連番・個数を同時に空)", async () => {
    authDisabled();
    renderWithRouter(<LotCreatePage />);
    const year = (await screen.findByLabelText("年度")) as HTMLInputElement;
    const seq = screen.getByLabelText("連番") as HTMLInputElement;
    const count = screen.getByLabelText("個数") as HTMLInputElement;
    fireEvent.change(year, { target: { value: "" } });
    fireEvent.change(seq, { target: { value: "" } });
    fireEvent.change(count, { target: { value: "" } });
    fireEvent.click(screen.getByRole("button", { name: "作成" }));
    // 3 つすべて aria-invalid=true になる (= 同時に invalid 表示)
    await waitFor(() => expect(year).toHaveAttribute("aria-invalid", "true"));
    expect(seq).toHaveAttribute("aria-invalid", "true");
    expect(count).toHaveAttribute("aria-invalid", "true");
  });

  it("FE-VAL-POLICY-004: 未操作 field は赤くしない (mount 直後 aria-invalid なし)", async () => {
    const data = makeSalesCase({ lots: ["2026-A-1"] });
    renderWithApp(
      <DirectAppraisalForm data={data} title="査定" buttonLabel="登録" onSubmit={vi.fn()} />,
    );
    // 全 input の aria-invalid を確認 — true は無い
    const allInputs = screen.getAllByRole("spinbutton").concat(screen.getAllByRole("textbox"));
    for (const input of allInputs) {
      expect(input).not.toHaveAttribute("aria-invalid", "true");
    }
  });

  it("FE-VAL-POLICY-005: 修正後はエラーを即消す (LotCreatePage `年度`)", async () => {
    authDisabled();
    renderWithRouter(<LotCreatePage />);
    const year = (await screen.findByLabelText("年度")) as HTMLInputElement;
    fireEvent.change(year, { target: { value: "" } });
    fireEvent.click(screen.getByRole("button", { name: "作成" }));
    await waitFor(() => expect(year).toHaveAttribute("aria-invalid", "true"));
    // 修正 — onTouched モードでは submit 後の change で再検証される
    fireEvent.change(year, { target: { value: "2026" } });
    await waitFor(() => expect(year).toHaveAttribute("aria-invalid", "false"));
  });

  it("FE-VAL-POLICY-006: submit 時に全 invalid へ aria-invalid=true、API 未呼出", async () => {
    authDisabled();
    renderWithRouter(<LotCreatePage />);
    const year = (await screen.findByLabelText("年度")) as HTMLInputElement;
    const seq = screen.getByLabelText("連番") as HTMLInputElement;
    fireEvent.change(year, { target: { value: "" } });
    fireEvent.change(seq, { target: { value: "" } });
    fireEvent.click(screen.getByRole("button", { name: "作成" }));
    await waitFor(() => expect(year).toHaveAttribute("aria-invalid", "true"));
    expect(seq).toHaveAttribute("aria-invalid", "true");
    // API 未呼出
    expect(requestsFor("/api/lots")).toHaveLength(0);
  });

  it("FE-VAL-POLICY-007: 不正ロット ID は全件列挙する (lotsSchema superRefine)", async () => {
    // UI 経由では LotSelectDialog が正しい lotNumber しか返さないため、
    // schema 直接呼びで「複数の invalid id が同一 issue にまとめて出る」
    // ことを検査する。
    const { lotsSchema } = await import("@/pages/sales-cases/sales-case-create-validation");
    const result = lotsSchema.safeParse(["ok-1-2", "BAD", "2026-A-1", "WRONG"]);
    expect(result.success).toBe(false);
    if (!result.success) {
      const message = result.error.issues.map((i) => i.message).join(" / ");
      expect(message).toContain("BAD");
      expect(message).toContain("WRONG");
    }
  });
});
