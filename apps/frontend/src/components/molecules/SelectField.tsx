/**
 * radix-ui Select を rhf Controller でラップした molecule。
 * 表示値は string で固定。事業部コードなどフォーム内部値が integer の
 * 場合は `parse: Number` を渡す。
 */
import { FormControl, FormField, FormItem, FormLabel, FormMessage } from "@/components/atoms/form";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/atoms/select";
import type { Control, FieldPath, FieldValues } from "react-hook-form";

export interface SelectFieldProps<T extends FieldValues> {
  control: Control<T>;
  name: FieldPath<T>;
  label: string;
  options: ReadonlyArray<readonly [value: string, label: string]>;
  placeholder?: string;
  /** 表示値 (string) を form 内部値に変換する。既定は素通し。 */
  parse?: (value: string) => unknown;
  /** form 内部値を <Select> 用の string 値に変換する。既定は `String(...)` */
  format?: (value: unknown) => string;
  /** field.onChange の後に呼ばれる任意のコールバック。連動 field のリセット等に。 */
  onAfterChange?: (value: string) => void;
}

export function SelectField<T extends FieldValues>({
  control,
  name,
  label,
  options,
  placeholder,
  parse,
  format,
  onAfterChange,
}: SelectFieldProps<T>) {
  return (
    <FormField
      control={control}
      name={name}
      render={({ field }) => {
        const stringValue = format
          ? format(field.value)
          : field.value != null
            ? String(field.value)
            : "";
        return (
          <FormItem className="space-y-1">
            <FormLabel>{label}</FormLabel>
            <Select
              value={stringValue}
              onValueChange={(v) => {
                field.onChange(parse ? parse(v) : v);
                onAfterChange?.(v);
              }}
            >
              <FormControl>
                <SelectTrigger className="w-full">
                  <SelectValue placeholder={placeholder} />
                </SelectTrigger>
              </FormControl>
              <SelectContent>
                {options.map(([optionValue, optionLabel]) => (
                  <SelectItem key={optionValue} value={optionValue}>
                    {optionLabel}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <FormMessage />
          </FormItem>
        );
      }}
    />
  );
}
