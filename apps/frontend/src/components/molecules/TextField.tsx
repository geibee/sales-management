/**
 * shadcn `atoms/form.tsx` の `FormField` + `FormControl` + `FormMessage`
 * を内部で使う rhf テキスト入力 molecule。
 *
 * 親 page は `<Form {...formMethods}>` (FormProvider) で囲んだうえで
 * `<TextField control={control} name="year" label="年度" />` のように
 * 呼ぶ。FormControl が `aria-invalid` と `aria-describedby` を自動付与
 * してくれるので、エラー時に input とメッセージが ARIA で紐付く
 * (Phase 2d FE-A11Y-FORM-003 を満たす)。
 */
import { FormControl, FormField, FormItem, FormLabel, FormMessage } from "@/components/atoms/form";
import { Input } from "@/components/atoms/input";
import type { Control, FieldPath, FieldValues } from "react-hook-form";

export interface TextFieldProps<T extends FieldValues> {
  control: Control<T>;
  name: FieldPath<T>;
  label: string;
  type?: string;
  required?: boolean;
  placeholder?: string;
}

export function TextField<T extends FieldValues>({
  control,
  name,
  label,
  type = "text",
  required = true,
  placeholder,
}: TextFieldProps<T>) {
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
            <Input type={type} placeholder={placeholder} {...field} value={field.value ?? ""} />
          </FormControl>
          <FormMessage />
        </FormItem>
      )}
    />
  );
}
