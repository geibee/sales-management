import { SalesCaseDetailPage } from "@/pages/sales-cases/SalesCaseDetailPage";
import { createFileRoute } from "@tanstack/react-router";

export const Route = createFileRoute("/sales-cases/$id")({
  component: SalesCaseRouteComponent,
});

function SalesCaseRouteComponent() {
  const { id } = Route.useParams();
  return <SalesCaseDetailPage id={id} />;
}
