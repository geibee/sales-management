import { Toaster } from "@/components/ui/sonner";
import { RouterProvider, createRouter } from "@tanstack/react-router";
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { SWRConfig } from "swr";
import { routeTree } from "./routeTree.gen";
import "./styles.css";

const router = createRouter({ routeTree });

declare module "@tanstack/react-router" {
  interface Register {
    router: typeof router;
  }
}

const rootEl = document.getElementById("root");
if (!rootEl) throw new Error("#root not found");

createRoot(rootEl).render(
  <StrictMode>
    <SWRConfig
      value={{
        revalidateOnFocus: true,
        dedupingInterval: 2000,
        shouldRetryOnError: false,
      }}
    >
      <RouterProvider router={router} />
      <Toaster position="top-right" />
    </SWRConfig>
  </StrictMode>,
);
