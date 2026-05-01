import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { describeApiError } from "@/lib/api-client";
import { useActionState } from "react";
import { toast } from "sonner";

type Props = {
  title: string;
  buttonLabel: string;
  withDate?: boolean;
  dateLabel?: string;
  withText?: boolean;
  textLabel?: string;
  textPlaceholder?: string;
  destructive?: boolean;
  disabled?: boolean;
  onSubmit: (date: string | undefined, text: string | undefined) => Promise<void>;
};

export function LotActionForm({
  title,
  buttonLabel,
  withDate,
  dateLabel,
  withText,
  textLabel,
  textPlaceholder,
  destructive,
  disabled,
  onSubmit,
}: Props) {
  const [, action, isPending] = useActionState(async (_prev: null, fd: FormData) => {
    const date = withDate ? String(fd.get("date") ?? "") : undefined;
    const text = withText ? String(fd.get("text") ?? "") : undefined;
    if (withDate && !date) {
      toast.error("日付を入力してください");
      return null;
    }
    if (withText && !text) {
      toast.error(`${textLabel ?? "値"}を入力してください`);
      return null;
    }
    try {
      await onSubmit(date, text);
      toast.success(`${title} を実行しました`);
    } catch (e) {
      toast.error(describeApiError(e));
    }
    return null;
  }, null);

  return (
    <Card aria-disabled={disabled} className={disabled ? "opacity-50" : undefined}>
      <CardHeader>
        <CardTitle className="text-base">{title}</CardTitle>
      </CardHeader>
      <CardContent>
        <form action={action} className="space-y-3">
          {withDate && (
            <div className="space-y-1">
              <Label htmlFor={`${title}-date`}>{dateLabel ?? "日付"}</Label>
              <Input id={`${title}-date`} type="date" name="date" required disabled={disabled} />
            </div>
          )}
          {withText && (
            <div className="space-y-1">
              <Label htmlFor={`${title}-text`}>{textLabel ?? "入力"}</Label>
              <Input
                id={`${title}-text`}
                type="text"
                name="text"
                required
                disabled={disabled}
                placeholder={textPlaceholder}
              />
            </div>
          )}
          <Button
            type="submit"
            disabled={disabled || isPending}
            variant={destructive ? "destructive" : "default"}
          >
            {isPending ? "実行中…" : buttonLabel}
          </Button>
        </form>
      </CardContent>
    </Card>
  );
}
