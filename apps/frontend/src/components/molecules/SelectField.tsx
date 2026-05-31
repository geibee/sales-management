/**
 * radix-ui Select の controlled 版 field。rhf `setValue` / `watch` 経由
 * で値を制御する page (LotCreatePage / SalesCaseCreatePage / Dialog
 * 系) で使う想定。旧 `LotCreatePage.SelectField` を抽出したもの。
 */
import { Label } from "@/components/atoms/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/atoms/select";
import { FieldError } from "./FieldError";

export interface SelectFieldProps {
  label: string;
  value: string;
  options: ReadonlyArray<readonly [value: string, label: string]>;
  onValueChange: (value: string) => void;
  error?: string;
  placeholder?: string;
}

export function SelectField({
  label,
  value,
  options,
  onValueChange,
  error,
  placeholder,
}: SelectFieldProps) {
  return (
    <div className="space-y-1">
      <Label>{label}</Label>
      <Select value={value} onValueChange={onValueChange}>
        <SelectTrigger className="w-full" aria-invalid={!!error}>
          <SelectValue placeholder={placeholder} />
        </SelectTrigger>
        <SelectContent>
          {options.map(([optionValue, optionLabel]) => (
            <SelectItem key={optionValue} value={optionValue}>
              {optionLabel}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
      <FieldError message={error} />
    </div>
  );
}
