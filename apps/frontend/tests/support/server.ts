/**
 * Phase 0 scaffold for MSW. The real server (request capture +
 * resetHandlers + default handlers) lands in Phase 1; this file only
 * exists so Phase 0 wiring (devDependency install + module presence)
 * can be exercised without coupling the rest of the suite to MSW yet.
 */
import { setupServer } from "msw/node";

export const server = setupServer();
