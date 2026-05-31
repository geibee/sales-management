import { z } from "zod";

const ITEM_CATEGORIES = ["general", "premium", "custom"] as const;
const INSPECTION_RESULTS = ["pass", "fail"] as const;

function emptyStringToUndefined(value: unknown): unknown {
  if (typeof value === "string" && value.trim() === "") return undefined;
  return value;
}

function numberInput(label: string, configure: (schema: z.ZodNumber) => z.ZodNumber = (s) => s) {
  return z.preprocess(
    emptyStringToUndefined,
    configure(
      z.coerce
        .number({
          required_error: `${label}を入力してください`,
          invalid_type_error: `${label}は数値で入力してください`,
        })
        .finite(`${label}は数値で入力してください`),
    ),
  );
}

function positiveIntInput(label: string) {
  return numberInput(label, (schema) =>
    schema.int(`${label}は整数で入力してください`).gt(0, `${label}は1以上で入力してください`),
  );
}

function textInput(label: string) {
  return z.string().trim().min(1, `${label}を入力してください`);
}

export const lotCreateFormSchema = z.object({
  year: positiveIntInput("年度"),
  location: textInput("保管場所"),
  seq: positiveIntInput("連番"),
  divisionCode: numberInput("事業部コード", (schema) =>
    schema.int("事業部コードは整数で入力してください"),
  ),
  departmentCode: numberInput("部コード", (schema) =>
    schema.int("部コードは整数で入力してください"),
  ),
  sectionCode: numberInput("課コード", (schema) => schema.int("課コードは整数で入力してください")),
  processCategory: numberInput("工程区分", (schema) =>
    schema.int("工程区分は整数で入力してください"),
  ),
  inspectionCategory: numberInput("検査区分", (schema) =>
    schema.int("検査区分は整数で入力してください"),
  ),
  manufacturingCategory: numberInput("製造区分", (schema) =>
    schema.int("製造区分は整数で入力してください"),
  ),
  itemCategory: z.enum(ITEM_CATEGORIES, {
    errorMap: () => ({ message: "品目区分を選択してください" }),
  }),
  premiumCategory: textInput("上位品区分"),
  productCategoryCode: textInput("商品分類コード"),
  lengthSpecLower: numberInput("長さ下限"),
  thicknessSpecLower: numberInput("太さ下限"),
  thicknessSpecUpper: numberInput("太さ上限"),
  qualityGrade: textInput("品質等級"),
  count: positiveIntInput("個数"),
  quantity: numberInput("数量", (schema) => schema.gte(0.001, "数量は0.001以上で入力してください")),
  inspectionResultCategory: z.enum(INSPECTION_RESULTS, {
    errorMap: () => ({ message: "検査結果を選択してください" }),
  }),
});

export type LotCreateFormValues = z.infer<typeof lotCreateFormSchema>;

export const lotCreateDefaultValues: LotCreateFormValues = {
  year: 2026,
  location: "A",
  seq: 1,
  divisionCode: 1,
  departmentCode: 1,
  sectionCode: 1,
  processCategory: 1,
  inspectionCategory: 1,
  manufacturingCategory: 1,
  itemCategory: "general",
  premiumCategory: "none",
  productCategoryCode: "default",
  lengthSpecLower: 1,
  thicknessSpecLower: 1,
  thicknessSpecUpper: 2,
  qualityGrade: "A",
  count: 10,
  quantity: 10,
  inspectionResultCategory: "pass",
};

export function toCreateLotBody(values: LotCreateFormValues) {
  return {
    lotNumber: {
      year: values.year,
      location: values.location,
      seq: values.seq,
    },
    divisionCode: values.divisionCode,
    departmentCode: values.departmentCode,
    sectionCode: values.sectionCode,
    processCategory: values.processCategory,
    inspectionCategory: values.inspectionCategory,
    manufacturingCategory: values.manufacturingCategory,
    details: [
      {
        itemCategory: values.itemCategory,
        premiumCategory: values.premiumCategory,
        productCategoryCode: values.productCategoryCode,
        lengthSpecLower: values.lengthSpecLower,
        thicknessSpecLower: values.thicknessSpecLower,
        thicknessSpecUpper: values.thicknessSpecUpper,
        qualityGrade: values.qualityGrade,
        count: values.count,
        quantity: values.quantity,
        inspectionResultCategory: values.inspectionResultCategory,
      },
    ],
  };
}
