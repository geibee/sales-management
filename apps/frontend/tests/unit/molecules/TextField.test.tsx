/**
 * `molecules/TextField` (rhf Controller + shadcn FormField/FormControl/
 * FormMessage) の表示契約を直接検証する。共通 a11y / Validation
 * ポリシーの "FE-A11Y-FORM-002 (aria-invalid)" /
 * "FE-A11Y-FORM-003 (aria-describedby)" / "FE-VAL-POLICY-005 (修正で
 * 即消し)" は本来 form 横断のルールだが、その根拠は molecule 1 個に
 * 集約されるので、ここで oracle として固定する。
 */
import { Form } from "@/components/atoms/form";
import { TextField } from "@/components/molecules/TextField";
import { zodResolver } from "@hookform/resolvers/zod";
import { fireEvent, screen, waitFor } from "@testing-library/react";
import { useForm } from "react-hook-form";
import { describe, expect, it } from "vitest";
import { z } from "zod";
import { renderWithApp } from "../../support/render";

const schema = z.object({ name: z.string().min(1, "名前を入力してください") });
type Values = z.infer<typeof schema>;

function Harness({ defaultValue = "" }: { defaultValue?: string }) {
  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { name: defaultValue },
    mode: "onTouched",
  });
  return (
    <Form {...form}>
      <form onSubmit={form.handleSubmit(() => {})} noValidate>
        <TextField control={form.control} name="name" label="名前" />
        <button type="submit">送信</button>
      </form>
    </Form>
  );
}

describe("<TextField> (molecule)", () => {
  it("mount 直後は aria-invalid なし、role=alert のメッセージも無い", () => {
    renderWithApp(<Harness />);
    const input = screen.getByLabelText("名前");
    expect(input).not.toHaveAttribute("aria-invalid", "true");
    expect(screen.queryByRole("alert")).toBeNull();
  });

  it("submit でバリデーション NG → aria-invalid=true、role=alert に message", async () => {
    renderWithApp(<Harness />);
    fireEvent.click(screen.getByRole("button", { name: "送信" }));
    const input = screen.getByLabelText("名前");
    await waitFor(() => expect(input).toHaveAttribute("aria-invalid", "true"));
    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("名前を入力してください");
  });

  it("aria-describedby が message id を含む (FE-A11Y-FORM-003)", async () => {
    renderWithApp(<Harness />);
    fireEvent.click(screen.getByRole("button", { name: "送信" }));
    const input = screen.getByLabelText("名前");
    await waitFor(() => expect(input).toHaveAttribute("aria-invalid", "true"));
    const describedBy = input.getAttribute("aria-describedby");
    expect(describedBy).toBeTruthy();
    const alert = screen.getByRole("alert");
    // shadcn FormMessage の id は FormItem の React.useId() ベース。
    // describedBy にその id が含まれていることを確認。
    expect(describedBy!.split(/\s+/)).toContain(alert.id);
  });

  it("入力修正で aria-invalid が false に戻る (FE-VAL-POLICY-005)", async () => {
    renderWithApp(<Harness />);
    fireEvent.click(screen.getByRole("button", { name: "送信" }));
    const input = screen.getByLabelText("名前");
    await waitFor(() => expect(input).toHaveAttribute("aria-invalid", "true"));
    fireEvent.change(input, { target: { value: "ok" } });
    await waitFor(() => expect(input).toHaveAttribute("aria-invalid", "false"));
    expect(screen.queryByRole("alert")).toBeNull();
  });

  it("`required=false` のとき label 末尾に「(任意)」が出る", () => {
    const optionalSchema = z.object({ name: z.string().optional() });
    function H() {
      const form = useForm<{ name?: string }>({
        resolver: zodResolver(optionalSchema),
        defaultValues: { name: "" },
        mode: "onTouched",
      });
      return (
        <Form {...form}>
          <TextField control={form.control} name="name" label="メモ" required={false} />
        </Form>
      );
    }
    renderWithApp(<H />);
    expect(screen.getByText("(任意)")).toBeInTheDocument();
  });
});
