import { ConsignmentCaseDetailPage } from "@/pages/consignment-cases/ConsignmentCaseDetailPage";
import { createFileRoute } from "@tanstack/react-router";

export const Route = createFileRoute("/consignment-cases/$id")({
  component: ConsignmentCaseRouteComponent,
});

function ConsignmentCaseRouteComponent() {
  const { id } = Route.useParams();
  return <ConsignmentCaseDetailPage id={id} />;
}
