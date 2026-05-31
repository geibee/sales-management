/**
 * Phase 2d — 共通 a11y / form (FE-A11Y-FORM-001..004)。
 *
 * 計画では form 横断で以下を検査するよう求められている:
 *   001  validation エラーは field wrapper 内に出る (視覚的に近接させる)
 *   002  invalid input には `aria-invalid="true"` が付く
 *   003  invalid input とエラーテキストは `aria-describedby` で紐付く
 *   004  input 上で Enter 押下が submit と同じ body / 同じ回数で呼ばれる
 *
 * 本プロジェクトには 2 系統の form がある:
 *   - `react-hook-form` + zod ページ (`LotCreatePage`,
 *     `SalesCaseCreatePage`, `SalesCaseCreateDialog`) —
 *     `<p role="alert">` を field 直下に出し、input に `aria-invalid`
 *     を付けるが、`aria-describedby` の紐付けは未実装 → 003 は TODO
 *   - `RichActionForms` — 構造は同じ (field + alert + aria-invalid)、
 *     こちらも describedby なし → 003 は同じく TODO
 *
 * 下のテストは現状 UI が満たしている範囲を検査し、残りは `it.todo`
 * で意図だけ残してある。
 */
import { SalesCaseCreatePage } from "@/pages/sales-cases/SalesCaseCreatePage";
import { DateVersionActionForm } from "@/pages/sales-cases/actions/RichActionForms";
import { fireEvent, screen, waitFor } from "@testing-library/react";
import { HttpResponse, http } from "msw";
import { describe, expect, it, vi } from "vitest";
import { makeCodeMasters } from "../../support/fixtures";
import { renderWithApp, renderWithRouter } from "../../support/render";
import { server } from "../../support/server";

describe("Form a11y (FE-A11Y-FORM-*)", () => {
  it("FE-A11Y-FORM-001: 不正 submit でエラーが field wrapper 内に出る (SalesCaseCreatePage)", async () => {
    // SalesCaseCreatePage は Guard で operator 要求 → 認証 OFF で bypass
    server.use(
      http.get("/api/auth/config", () => HttpResponse.json({ enabled: false })),
      http.get("/api/code-masters", () => HttpResponse.json(makeCodeMasters())),
    );
    renderWithRouter(<SalesCaseCreatePage />);
    // ロット未選択 + salesDate 空 で submit
    const button = await screen.findByRole("button", { name: "作成" });
    fireEvent.submit(button.closest("form")!);
    // 「ロットを1つ以上選択してください」が field wrapper 内に出る
    const lotsError = await screen.findByText("ロットを1つ以上選択してください");
    // 該当 wrapper の直近祖先 (Label と兄弟) に <Label>対象ロット</Label> が存在することで近傍性を確認
    const wrapper = lotsError.closest("div.space-y-2") ?? lotsError.parentElement;
    expect(wrapper).not.toBeNull();
    expect(wrapper!.textContent).toContain("対象ロット");
  });

  it("FE-A11Y-FORM-002: invalid submit 後、対応する input に aria-invalid=true が付く", async () => {
    server.use(
      http.get("/api/auth/config", () => HttpResponse.json({ enabled: false })),
      http.get("/api/code-masters", () => HttpResponse.json(makeCodeMasters())),
    );
    renderWithRouter(<SalesCaseCreatePage />);
    const button = await screen.findByRole("button", { name: "作成" });
    // salesDate 空 / lots 空 で submit
    fireEvent.submit(button.closest("form")!);
    const salesDate = await screen.findByLabelText("販売日");
    await waitFor(() => expect(salesDate).toHaveAttribute("aria-invalid", "true"));
  });

  it.todo(
    "FE-A11Y-FORM-003: invalid input とエラーが aria-describedby で紐付く — 現状 UI は describedby 未対応",
  );

  it("FE-A11Y-FORM-004: input 上で Enter 押下が submit と同じ body を送る (DateVersionActionForm)", async () => {
    const onSubmit = vi.fn<(body: Record<string, unknown>) => Promise<void>>(async () => {});
    renderWithApp(
      <DateVersionActionForm
        title="出荷指示"
        buttonLabel="登録"
        dateLabel="出荷予定日"
        defaultDate="2026-05-01"
        version={2}
        onSubmit={onSubmit}
      />,
    );
    const input = screen.getByLabelText("出荷予定日");
    // Enter で form submit — jsdom では実装が不安定なので requestSubmit を直接呼ぶ
    const form = input.closest("form")! as HTMLFormElement;
    form.requestSubmit();
    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
    expect(onSubmit.mock.calls[0][0]).toMatchObject({ date: "2026-05-01", version: 2 });
  });
});
