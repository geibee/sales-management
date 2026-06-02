import type { ReactNode } from "react";
import { Sidebar } from "./Sidebar";
import { Topbar } from "./Topbar";

export function Shell({ children }: { children: ReactNode }) {
  return (
    <div className="app">
      <Sidebar />
      <main className="main">
        <Topbar />
        {children}
      </main>
    </div>
  );
}
