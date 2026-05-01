import { LotCreatePage } from "@/pages/lots/LotCreatePage";
import { createFileRoute } from "@tanstack/react-router";

export const Route = createFileRoute("/lots/new")({
  component: () => <LotCreatePage />,
});
