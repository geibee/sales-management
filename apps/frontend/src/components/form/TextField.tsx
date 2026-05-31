/**
 * react-hook-form `register("name")` を受けて、ラベル + 入力 +
 * `FieldError` の 3 点セットを描画する共通 field。
 *
 * 旧 `LotCreatePage.TextField` / `SalesCaseCreatePage.TextField` の
 * union シグネチャ:
 *   - `type` は text / date / email 等を選べる (default "text")
 *   - `required=false` のとき label 末尾に「(任意)」を付ける
 *     (旧 RichActionForms.TextField 由来)
 */
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import type { UseFormRegisterReturn } from "react-hook-form";
import { FieldError } from "./FieldError";

export interface TextFieldProps {
  label: string;
  registration: UseFormRegisterReturn;
  error?: string;
  type?: string;
  required?: boolean;
}

export function TextField({
  label,
  registration,
  error,
  type = "text",
  required = true,
}: TextFieldProps) {
  return (
    <div className="space-y-1">
      <Label htmlFor={registration.name}>
        {label}
        {!required && <span className="ml-1 text-muted-foreground text-xs">(任意)</span>}
      </Label>
      <Input id={registration.name} type={type} aria-invalid={!!error} {...registration} />
      <FieldError message={error} />
    </div>
  );
}
