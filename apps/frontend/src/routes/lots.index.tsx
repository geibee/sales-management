import { LotListPage } from "@/pages/lots/LotListPage";
import { createFileRoute } from "@tanstack/react-router";

export const Route = createFileRoute("/lots/")({
  component: () => <LotListPage />,
});
