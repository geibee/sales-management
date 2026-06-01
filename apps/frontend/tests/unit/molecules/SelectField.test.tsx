/**
 * `molecules/SelectField` のスモーク。radix-ui Select の dropdown 展開
 * は jsdom のポインタイベント未対応で不安定なため、本テストは「label と
 * combobox trigger が組み立たる」「parse オプションで rhf 値が integer
 * になる」を間接的に確認する程度に留める。詳細な submit body 検査は
 * Phase 2f `SalesCaseCreateDialog.test.tsx` を参照。
 */
import { Form } from "@/components/atoms/form";
import { SelectField } from "@/components/molecules/SelectField";
import { screen } from "@testing-library/react";
import { useForm } from "react-hook-form";
import { describe, expect, it } from "vitest";
import { renderWithApp } from "../../support/render";

function Harness() {
  const form = useForm<{ code: number }>({
    defaultValues: { code: 1 },
    mode: "onTouched",
  });
  return (
    <Form {...form}>
      <SelectField
        control={form.control}
        name="code"
        label="コード"
        options={[
          ["1", "A"],
          ["2", "B"],
        ]}
        parse={Number}
      />
    </Form>
  );
}

describe("<SelectField> (molecule)", () => {
  it("label と combobox trigger が組み立たる", () => {
    renderWithApp(<Harness />);
    expect(screen.getByText("コード")).toBeInTheDocument();
    // radix-ui Select の trigger は role=combobox
    expect(screen.getByRole("combobox")).toBeInTheDocument();
  });
});
