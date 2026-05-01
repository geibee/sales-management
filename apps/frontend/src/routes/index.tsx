import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Link, createFileRoute } from "@tanstack/react-router";

export const Route = createFileRoute("/")({
  component: HomePage,
});

type Aggregate = {
  title: string;
  description: string;
  newHref?: "/lots/new" | "/sales-cases/new" | "/external/price-check";
  listHref?: "/lots" | "/sales-cases";
  listLabel?: string;
};

const aggregates: Aggregate[] = [
  {
    title: "在庫ロット",
    description: "ロット作成・状態遷移・CSV エクスポート",
    newHref: "/lots/new",
    listHref: "/lots",
    listLabel: "一覧",
  },
  {
    title: "販売案件",
    description: "直接販売・予約・委託の各案件 (種別フィルタで切替)",
    newHref: "/sales-cases/new",
    listHref: "/sales-cases",
    listLabel: "一覧",
  },
  {
    title: "外部価格チェック",
    description: "外部価格 API への問い合わせ",
    newHref: "/external/price-check",
  },
];

function HomePage() {
  return (
    <div className="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3">
      {aggregates.map((a) => (
        <Card key={a.title}>
          <CardHeader>
            <CardTitle>{a.title}</CardTitle>
            <CardDescription>{a.description}</CardDescription>
          </CardHeader>
          <CardContent className="flex gap-3">
            {a.listHref && (
              <Link to={a.listHref} className="text-sm font-medium underline underline-offset-4">
                {a.listLabel ?? "一覧"}
              </Link>
            )}
            {a.newHref && (
              <Link to={a.newHref} className="text-sm font-medium underline underline-offset-4">
                新規作成
              </Link>
            )}
          </CardContent>
        </Card>
      ))}
    </div>
  );
}
