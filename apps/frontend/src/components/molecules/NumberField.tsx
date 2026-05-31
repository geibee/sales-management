/**
 * 数値入力 field。旧 `LotCreatePage.NumberField` /
 * `RichActionForms.NumberField` の union シグネチャ:
 *   - `step` / `min` / `max` 任意
 *   - `required=false` で「(任意)」表示
 */
import { Input } from "@/components/atoms/input";
import { Label } from "@/components/atoms/label";
import type { UseFormRegisterReturn } from "react-hook-form";
import { FieldError } from "./FieldError";

export interface NumberFieldProps {
  label: string;
  registration: UseFormRegisterReturn;
  error?: string;
  step?: string;
  min?: number;
  max?: number;
  required?: boolean;
}

export function NumberField({
  label,
  registration,
  error,
  step,
  min,
  max,
  required = true,
}: NumberFieldProps) {
  return (
    <div className="space-y-1">
      <Label htmlFor={registration.name}>
        {label}
        {!required && <span className="ml-1 text-muted-foreground text-xs">(任意)</span>}
      </Label>
      <Input
        id={registration.name}
        type="number"
        step={step}
        min={min}
        max={max}
        aria-invalid={!!error}
        {...registration}
      />
      <FieldError message={error} />
    </div>
  );
}
