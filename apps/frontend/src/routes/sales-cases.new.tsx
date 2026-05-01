import { SalesCaseCreatePage } from "@/pages/sales-cases/SalesCaseCreatePage";
import { createFileRoute } from "@tanstack/react-router";

export const Route = createFileRoute("/sales-cases/new")({
  component: () => <SalesCaseCreatePage />,
});
