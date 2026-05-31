/**
 * 数値入力 molecule。`TextField` と同じく shadcn FormField + FormControl
 * の上に乗っているので、`aria-invalid` と `aria-describedby` が自動で付く。
 */
import { FormControl, FormField, FormItem, FormLabel, FormMessage } from "@/components/atoms/form";
import { Input } from "@/components/atoms/input";
import type { Control, FieldPath, FieldValues } from "react-hook-form";

export interface NumberFieldProps<T extends FieldValues> {
  control: Control<T>;
  name: FieldPath<T>;
  label: string;
  step?: string;
  min?: number;
  max?: number;
  required?: boolean;
}

export function NumberField<T extends FieldValues>({
  control,
  name,
  label,
  step,
  min,
  max,
  required = true,
}: NumberFieldProps<T>) {
  return (
    <FormField
      control={control}
      name={name}
      render={({ field }) => (
        <FormItem className="space-y-1">
          <FormLabel>
            {label}
            {!required && <span className="ml-1 text-muted-foreground text-xs">(任意)</span>}
          </FormLabel>
          <FormControl>
            <Input
              type="number"
              step={step}
              min={min}
              max={max}
              {...field}
              value={field.value ?? ""}
            />
          </FormControl>
          <FormMessage />
        </FormItem>
      )}
    />
  );
}
