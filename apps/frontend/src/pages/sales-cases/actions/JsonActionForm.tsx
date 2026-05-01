import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Label } from "@/components/ui/label";
import { describeApiError } from "@/lib/api-client";
import { useActionState } from "react";
import { toast } from "sonner";

type Props = {
  title: string;
  buttonLabel: string;
  placeholder?: string;
  onSubmit: (body: Record<string, unknown>) => Promise<void>;
};

/**
 * Lightweight JSON-body form for state-transition endpoints whose request
 * shape is large and varies. Phase 1 ships a textarea so operators can paste
 * payloads; Phase 2 will replace each with a typed react-hook-form per
 * aggregate.
 */
export function JsonActionForm({ title, buttonLabel, placeholder, onSubmit }: Props) {
  const [, action, isPending] = useActionState(async (_prev: null, fd: FormData) => {
    const raw = String(fd.get("payload") ?? "{}").trim() || "{}";
    let parsed: Record<string, unknown>;
    try {
      parsed = JSON.parse(raw);
    } catch {
      toast.error("JSON が不正です");
      return null;
    }
    try {
      await onSubmit(parsed);
      toast.success(`${title} を実行しました`);
    } catch (e) {
      toast.error(describeApiError(e));
    }
    return null;
  }, null);

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">{title}</CardTitle>
      </CardHeader>
      <CardContent>
        <form action={action} className="space-y-3">
          <div className="space-y-1">
            <Label htmlFor={`${title}-body`}>リクエストボディ (JSON)</Label>
            <textarea
              id={`${title}-body`}
              name="payload"
              rows={4}
              placeholder={placeholder ?? "{}"}
              className="w-full rounded-md border bg-transparent p-2 font-mono text-xs"
              defaultValue="{}"
            />
          </div>
          <Button type="submit" disabled={isPending}>
            {isPending ? "実行中…" : buttonLabel}
          </Button>
        </form>
      </CardContent>
    </Card>
  );
}
