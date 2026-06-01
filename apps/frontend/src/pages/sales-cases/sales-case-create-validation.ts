import { z } from "zod";

export const CASE_TYPES = ["direct", "reservation", "consignment"] as const;
export type CaseType = (typeof CASE_TYPES)[number];

export const CASE_TYPE_OPTIONS: Array<[CaseType, string]> = [
  ["direct", "直接販売"],
  ["reservation", "予約"],
  ["consignment", "委託"],
];

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

// ---- 個別フィールドスキーマ（ページ／モーダルで共有） ----
export const caseTypeSchema = z.enum(CASE_TYPES, {
  errorMap: () => ({ message: "案件種別を選択してください" }),
});
export const divisionCodeSchema = positiveIntInput("事業部コード");
export const salesDateSchema = z
  .string()
  .trim()
  .refine(isIsoDate, "販売日は yyyy-MM-dd 形式で入力してください");
export const lotsSchema = z
  .array(z.string())
  .min(1, "ロットを1つ以上選択してください")
  .superRefine((lots, ctx) => {
    const invalid = lots.filter((lotNumber) => !LOT_NUMBER_PATTERN.test(lotNumber));
    if (invalid.length > 0) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: `ロット ID は 年度-保管場所-連番 の形式で入力してください: ${invalid.join(", ")}`,
      });
    }
  });

/** ロットを選択式で持つ作成フォーム（販売案件作成ページ用）。 */
export const salesCaseCreateFormSchema = z.object({
  caseType: caseTypeSchema,
  lots: lotsSchema,
  divisionCode: divisionCodeSchema,
  salesDate: salesDateSchema,
});

/** ロット一覧から選択済みのロットで起動するモーダル用（lots は props で受け取る）。 */
export const salesCaseCreateModalSchema = z.object({
  caseType: caseTypeSchema,
  divisionCode: divisionCodeSchema,
  salesDate: salesDateSchema,
});

export type SalesCaseCreateFormValues = z.infer<typeof salesCaseCreateFormSchema>;
export type SalesCaseCreateModalValues = z.infer<typeof salesCaseCreateModalSchema>;

export const salesCaseCreateDefaultValues: SalesCaseCreateFormValues = {
  caseType: "direct",
  lots: [],
  divisionCode: 1,
  salesDate: "",
};

export const salesCaseCreateModalDefaultValues: SalesCaseCreateModalValues = {
  caseType: "direct",
  divisionCode: 1,
  salesDate: "",
};

type CreateSalesCaseBody = {
  lots: string[];
  divisionCode: number;
  salesDate: string;
  caseType: CaseType;
};

export function toCreateSalesCaseBody(values: SalesCaseCreateFormValues): CreateSalesCaseBody {
  return {
    lots: values.lots,
    divisionCode: values.divisionCode,
    salesDate: values.salesDate,
    caseType: values.caseType,
  };
}

/** 案件種別に応じた詳細ページのルート。 */
export function caseDetailRoute(
  caseType: CaseType,
): "/sales-cases/$id" | "/reservation-cases/$id" | "/consignment-cases/$id" {
  if (caseType === "reservation") return "/reservation-cases/$id";
  if (caseType === "consignment") return "/consignment-cases/$id";
  return "/sales-cases/$id";
}
