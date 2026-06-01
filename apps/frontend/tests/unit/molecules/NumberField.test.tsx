/**
 * `molecules/NumberField` のスモーク。詳細は TextField と同じ pipeline
 * なので、ここでは「数値入力として描画される」「min/max/step 属性が
 * 伝搬する」だけを oracle にする。
 */
import { Form } from "@/components/atoms/form";
import { NumberField } from "@/components/molecules/NumberField";
import { screen } from "@testing-library/react";
import { useForm } from "react-hook-form";
import { describe, expect, it } from "vitest";
import { renderWithApp } from "../../support/render";

function Harness() {
  const form = useForm<{ qty: number }>({
    defaultValues: { qty: 10 },
    mode: "onTouched",
  });
  return (
    <Form {...form}>
      <NumberField control={form.control} name="qty" label="数量" min={0} max={100} step="0.5" />
    </Form>
  );
}

describe("<NumberField> (molecule)", () => {
  it("type=number、初期値、min/max/step が DOM に伝搬する", () => {
    renderWithApp(<Harness />);
    const input = screen.getByLabelText("数量") as HTMLInputElement;
    expect(input.type).toBe("number");
    expect(input.value).toBe("10");
    expect(input).toHaveAttribute("min", "0");
    expect(input).toHaveAttribute("max", "100");
    expect(input).toHaveAttribute("step", "0.5");
  });
});
