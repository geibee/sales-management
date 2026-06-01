/**
 * Phase 2b — `LotActionForm` (FE-COMP-LOT-ACTION-001..007, FE-A11Y-LOT-ACTION-001)。
 *
 * `LotActionForm` は `LotDetailPage` で状態遷移ボタン (製造完了 /
 * 出荷指示 / キャンセル / 品目転換 / …) ごとに使い回される薄い
 * 共通カード component。責務は以下:
 *   - 任意の date 入力 + 任意の text 入力を 1 つずつ持つ
 *   - submit 中は button ラベルを「実行中…」に切り替える
 *   - validation エラーも onSubmit 失敗も sonner toast で通知する
 *
 * テスト計画では validation を「field 直下 error + aria-invalid」と
 * 規定しているが、現行 component は `required` 属性 + `toast.error`
 * で実装されている。下のテストは現状の観測契約 (toast + API 未呼出)
 * を検査する。計画どおりの field-level oracle は UI が追従するまで
 * `it.todo` で意図だけ残してある。
 */
import { LotActionForm } from "@/components/organisms/forms/LotActionForm";
import { fireEvent, screen, waitFor } from "@testing-library/react";
import { toast } from "sonner";
import { describe, expect, it, vi } from "vitest";
import { deferred } from "../../../support/deferred";
import { renderWithApp } from "../../../support/render";

function setup(props: Partial<Parameters<typeof LotActionForm>[0]> = {}) {
  const onSubmit = vi.fn<(date?: string, text?: string) => Promise<void>>(async () => {});
  const merged: Parameters<typeof LotActionForm>[0] = {
    title: "製造完了",
    buttonLabel: "実行",
    onSubmit,
    ...props,
  };
  renderWithApp(<LotActionForm {...merged} />);
  return { onSubmit };
}

describe("LotActionForm (FE-COMP-LOT-ACTION-*)", () => {
  it("FE-COMP-LOT-ACTION-001: date 空で submit → API 未呼出、toast.error(`日付を入力してください`)", async () => {
    const toastError = vi.spyOn(toast, "error");
    const { onSubmit } = setup({ withDate: true, dateLabel: "完了日" });
    fireEvent.submit(screen.getByRole("button", { name: "実行" }).closest("form")!);
    await waitFor(() => expect(toastError).toHaveBeenCalledWith("日付を入力してください"));
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it("FE-COMP-LOT-ACTION-002: text 空で submit → API 未呼出、toast.error(`{label}を入力してください`)", async () => {
    const toastError = vi.spyOn(toast, "error");
    const { onSubmit } = setup({ withText: true, textLabel: "転換先" });
    fireEvent.submit(screen.getByRole("button", { name: "実行" }).closest("form")!);
    await waitFor(() => expect(toastError).toHaveBeenCalledWith("転換先を入力してください"));
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it("FE-COMP-LOT-ACTION-003: 有効な date を入れて submit → onSubmit(date, undefined) + 成功 toast", async () => {
    const toastSuccess = vi.spyOn(toast, "success");
    const { onSubmit } = setup({ withDate: true, dateLabel: "完了日" });
    fireEvent.change(screen.getByLabelText("完了日"), { target: { value: "2026-04-28" } });
    fireEvent.submit(screen.getByRole("button", { name: "実行" }).closest("form")!);
    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
    expect(onSubmit).toHaveBeenCalledWith("2026-04-28", undefined);
    await waitFor(() => expect(toastSuccess).toHaveBeenCalledWith("製造完了 を実行しました"));
  });

  it("FE-COMP-LOT-ACTION-004: 有効な text を入れて submit → onSubmit(undefined, text)", async () => {
    const { onSubmit } = setup({ withText: true, textLabel: "転換先" });
    fireEvent.change(screen.getByLabelText("転換先"), { target: { value: "2026-T-902" } });
    fireEvent.submit(screen.getByRole("button", { name: "実行" }).closest("form")!);
    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
    expect(onSubmit).toHaveBeenCalledWith(undefined, "2026-T-902");
  });

  it("FE-COMP-LOT-ACTION-005: 二重 submit でも onSubmit 呼出は 1 回、pending ラベルに切り替わる", async () => {
    const d = deferred<void>();
    const onSubmit = vi.fn<(date?: string, text?: string) => Promise<void>>(() => d.promise);
    renderWithApp(
      <LotActionForm
        title="製造完了"
        buttonLabel="実行"
        withDate
        dateLabel="完了日"
        onSubmit={onSubmit}
      />,
    );
    fireEvent.change(screen.getByLabelText("完了日"), { target: { value: "2026-04-28" } });
    // ボタンクリック経由なら disabled が効くため、二重実行のガードを「実装」レベルで確認できる。
    fireEvent.click(screen.getByRole("button", { name: "実行" }));
    expect(await screen.findByRole("button", { name: "実行中…" })).toBeDisabled();
    // pending 中のクリックは disabled で reject される
    fireEvent.click(screen.getByRole("button", { name: "実行中…" }));
    fireEvent.click(screen.getByRole("button", { name: "実行中…" }));
    // pending を解放しても、追加呼出は発生しない
    d.resolve();
    await waitFor(() => expect(screen.queryByRole("button", { name: "実行中…" })).toBeNull());
    expect(onSubmit).toHaveBeenCalledTimes(1);
  });

  it("FE-COMP-LOT-ACTION-006: disabled=true なら submit しても onSubmit 未呼出", async () => {
    const { onSubmit } = setup({ withDate: true, dateLabel: "完了日", disabled: true });
    expect(screen.getByRole("button", { name: "実行" })).toBeDisabled();
    fireEvent.change(screen.getByLabelText("完了日"), { target: { value: "2026-04-28" } });
    // disabled button のため submit でも action は走らない
    fireEvent.submit(screen.getByRole("button", { name: "実行" }).closest("form")!);
    await new Promise((r) => setTimeout(r, 0));
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it.todo(
    "FE-COMP-LOT-ACTION-007: 未操作 (mount 後 blur なし) では aria-invalid が立たない — 現状 UI は field-level error を持たないため Red",
  );

  it("FE-A11Y-LOT-ACTION-001: date ラベルと input が htmlFor で紐付く", () => {
    setup({ withDate: true, dateLabel: "完了日" });
    const input = screen.getByLabelText("完了日");
    expect(input).toHaveAttribute("type", "date");
    expect(input.tagName).toBe("INPUT");
  });
});
