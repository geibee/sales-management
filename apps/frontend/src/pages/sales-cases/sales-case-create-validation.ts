import { z } from "zod";

export const CASE_TYPES = ["direct", "reservation", "consignment"] as const;

const LOT_NUMBER_PATTERN = /^\d+-[^-\s]+-\d+$/;
const ISO_DATE_PATTERN = /^(\d{4})-(\d{2})-(\d{2})$/;

function emptyStringToUndefined(value: unknown): unknown {
  if (typeof value === "string" && value.trim() === "") return undefined;
  return value;
}

function positiveIntInput(label: string) {
  return z.preprocess(
    emptyStringToUndefined,
    z.coerce
      .number({
        required_error: `${label}を入力してください`,
        invalid_type_error: `${label}は数値で入力してください`,
      })
      .finite(`${label}は数値で入力してください`)
      .int(`${label}は整数で入力してください`)
      .gt(0, `${label}は1以上で入力してください`),
  );
}

function isIsoDate(value: string): boolean {
  const match = ISO_DATE_PATTERN.exec(value);
  if (!match) return false;

  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  const date = new Date(Date.UTC(year, month - 1, day));

  return (
    date.getUTCFullYear() === year && date.getUTCMonth() === month - 1 && date.getUTCDate() === day
  );
}

export function parseLotNumbers(value: string): string[] {
  return value
    .split(/[\n,]/)
    .map((item) => item.trim())
    .filter(Boolean);
}

export const salesCaseCreateFormSchema = z.object({
  caseType: z.enum(CASE_TYPES, {
    errorMap: () => ({ message: "案件種別を選択してください" }),
  }),
  lotsText: z.string().superRefine((value, ctx) => {
    const lotNumbers = parseLotNumbers(value);

    if (lotNumbers.length === 0) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: "ロット ID を1つ以上入力してください",
      });
      return;
    }

    const invalid = lotNumbers.filter((lotNumber) => !LOT_NUMBER_PATTERN.test(lotNumber));
    if (invalid.length > 0) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: `ロット ID は 年度-保管場所-連番 の形式で入力してください: ${invalid.join(", ")}`,
      });
    }
  }),
  divisionCode: positiveIntInput("事業部コード"),
  salesDate: z.string().trim().refine(isIsoDate, "販売日は yyyy-MM-dd 形式で入力してください"),
});

export type SalesCaseCreateFormValues = z.infer<typeof salesCaseCreateFormSchema>;

export const salesCaseCreateDefaultValues: SalesCaseCreateFormValues = {
  caseType: "direct",
  lotsText: "",
  divisionCode: 1,
  salesDate: "",
};

export function toCreateSalesCaseBody(values: SalesCaseCreateFormValues) {
  return {
    lots: parseLotNumbers(values.lotsText),
    divisionCode: values.divisionCode,
    salesDate: values.salesDate,
    caseType: values.caseType,
  };
}
