/**
 * `molecules/FieldError` は引数 `message` 1 つの極小 component。
 * 振る舞いは「message 有 → role=alert で赤字表示、message 無 → null」。
 */
import { FieldError } from "@/components/molecules/FieldError";
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

describe("<FieldError> (molecule)", () => {
  it("message があれば role=alert で表示する", () => {
    render(<FieldError message="必須項目です" />);
    expect(screen.getByRole("alert")).toHaveTextContent("必須項目です");
  });

  it("message が undefined / 空文字なら何も描画しない", () => {
    const { rerender, container } = render(<FieldError message={undefined} />);
    expect(container.firstChild).toBeNull();
    rerender(<FieldError message="" />);
    expect(container.firstChild).toBeNull();
  });
});
