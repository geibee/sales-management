import { LotDetailPage } from "@/pages/lots/LotDetailPage";
import { createFileRoute } from "@tanstack/react-router";

export const Route = createFileRoute("/lots/$id")({
  component: LotRouteComponent,
});

function LotRouteComponent() {
  const { id } = Route.useParams();
  return <LotDetailPage id={id} />;
}
