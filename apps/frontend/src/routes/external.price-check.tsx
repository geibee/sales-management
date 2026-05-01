import { PriceCheckPage } from "@/pages/external/PriceCheckPage";
import { createFileRoute } from "@tanstack/react-router";

export const Route = createFileRoute("/external/price-check")({
  component: () => <PriceCheckPage />,
});
