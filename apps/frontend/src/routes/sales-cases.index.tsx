import { SalesCaseListPage } from "@/pages/sales-cases/SalesCaseListPage";
import { createFileRoute } from "@tanstack/react-router";

export const Route = createFileRoute("/sales-cases/")({
  component: () => <SalesCaseListPage />,
});
