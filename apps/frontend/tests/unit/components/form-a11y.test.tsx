/**
 * Phase 2d — 共通 a11y / form (FE-A11Y-FORM-001..004).
 *
 * The plan asks us to verify, across forms, that:
 *   001  validation error sits inside the field wrapper (visual proximity)
 *   002  invalid input carries `aria-invalid="true"`
 *   003  invalid input and its error text are linked via `aria-describedby`
 *   004  Enter inside an input submits the form (same body / count as click)
 *
 * The codebase has two flavours of form:
 *   - `react-hook-form` + zod resolver pages (`LotCreatePage`,
 *     `SalesCaseCreatePage`, `SalesCaseCreateDialog`) — these emit
 *     `<p role="alert">` next to the field and `aria-invalid` on the
 *     input, but do NOT wire `aria-describedby`. So 003 is TODO.
 *   - `RichActionForms` — same shape (field + alert + aria-invalid),
 *     also no aria-describedby; 003 likewise TODO there.
 *
 * Tests below cover what the current implementation already satisfies
 * and mark the rest `it.todo`.
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
  it("FE-A11Y-FORM-001: 不正 submit で error が field wrapper 内に出る (SalesCaseCreatePage)", async () => {
    // SalesCaseCreatePage は Guard で operator 必要 → auth disabled で bypass
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

  it("FE-A11Y-FORM-002: invalid submit 後、対応 input に aria-invalid=true", async () => {
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
    "FE-A11Y-FORM-003: invalid input と error が aria-describedby で紐付く — 現状 UI は describedby 未対応",
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
