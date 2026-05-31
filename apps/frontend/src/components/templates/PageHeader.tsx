import { Button } from "@/components/atoms/button";
import { Link } from "@tanstack/react-router";
import { ArrowLeft } from "lucide-react";
/**
 * page の見出し領域を統一する薄い template。各 page に
 * 散らばっていた「アイコン付きタイトル + 説明文 + 戻るリンク」の
 * 組み立てを 1 か所にまとめる。
 *
 * 厚い template ではないので、page 側で組み立てを変えたい場合
 * (例: 右端にバッジを追加したい) は `actions` slot を埋めるか、
 * このコンポーネントを使わず Card と組み合わせて自由に書けばよい。
 */
import type { ReactNode } from "react";

export interface PageHeaderProps {
  title: ReactNode;
  description?: ReactNode;
  /** 任意。指定すると「← {backLabel ?? "一覧"}」リンクを右端に出す。 */
  backTo?: string;
  backLabel?: string;
  /** 右端 (backTo の左) に追加で並べる任意の要素。 */
  actions?: ReactNode;
}

export function PageHeader({
  title,
  description,
  backTo,
  backLabel = "一覧",
  actions,
}: PageHeaderProps) {
  return (
    <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
      <div className="space-y-1">
        <h1 className="flex items-center gap-2 font-semibold text-xl tracking-normal">{title}</h1>
        {description && <p className="text-muted-foreground text-sm">{description}</p>}
      </div>
      <div className="flex items-center gap-2">
        {actions}
        {backTo && (
          <Button asChild variant="outline" size="sm">
            <Link to={backTo}>
              <ArrowLeft className="size-4" />
              {backLabel}
            </Link>
          </Button>
        )}
      </div>
    </div>
  );
}
