import { ReservationCaseDetailPage } from "@/pages/reservation-cases/ReservationCaseDetailPage";
import { createFileRoute } from "@tanstack/react-router";

export const Route = createFileRoute("/reservation-cases/$id")({
  component: ReservationCaseRouteComponent,
});

function ReservationCaseRouteComponent() {
  const { id } = Route.useParams();
  return <ReservationCaseDetailPage id={id} />;
}
