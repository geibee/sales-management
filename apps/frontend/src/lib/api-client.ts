import { useAuth } from "@/stores/auth-store";
import type { z } from "zod";

const BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? "/api";

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    public readonly body: unknown,
    public readonly path: string,
  ) {
    super(`API ${status} ${path}`);
  }
}

function authHeaders(): Record<string, string> {
  const token = useAuth.getState().token;
  return token ? { authorization: `Bearer ${token}` } : {};
}

async function safeJson(res: Response): Promise<unknown> {
  try {
    return await res.json();
  } catch {
    return null;
  }
}

async function handle<T>(res: Response, path: string, schema?: z.ZodType<T>): Promise<T> {
  if (res.status === 401) {
    useAuth.getState().clear();
    throw new ApiError(401, await safeJson(res), path);
  }
  if (!res.ok) {
    throw new ApiError(res.status, await safeJson(res), path);
  }
  if (!schema) {
    return undefined as T;
  }
  const json = (await safeJson(res)) ?? {};
  return schema.parse(json);
}

export async function apiGet<T>(path: string, schema: z.ZodType<T>): Promise<T> {
  const res = await fetch(`${BASE}${path}`, { headers: authHeaders() });
  return handle(res, path, schema);
}

export async function apiSend<T = void>(
  method: "POST" | "PUT" | "DELETE",
  path: string,
  body?: unknown,
  schema?: z.ZodType<T>,
): Promise<T> {
  const res = await fetch(`${BASE}${path}`, {
    method,
    headers: { "content-type": "application/json", ...authHeaders() },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });
  return handle(res, path, schema);
}

/**
 * For binary / file downloads (e.g. CSV export). Triggers a browser download
 * via a transient anchor element.
 */
export async function apiDownload(path: string, suggestedName: string): Promise<void> {
  const res = await fetch(`${BASE}${path}`, { headers: authHeaders() });
  if (!res.ok) {
    throw new ApiError(res.status, await safeJson(res), path);
  }
  const blob = await res.blob();
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = suggestedName;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}

/**
 * Recognised problem+json `type` values that signal an optimistic-lock
 * (version-mismatch) conflict. Backend currently emits `optimistic-lock-conflict`
 * (see ProblemDetails.fs); the canonical URI form is also accepted so future
 * IdP/middleware changes can adopt the longer form without breaking the UI.
 */
const VERSION_MISMATCH_TYPES = new Set<string>([
  "optimistic-lock-conflict",
  "https://errors.example.com/version-mismatch",
]);

/**
 * Returns true when `err` is a 409 problem+json indicating an optimistic-lock
 * (version) conflict. Callers use this to refetch the resource and surface a
 * dedicated toast.
 */
export function isVersionMismatch(err: unknown): boolean {
  if (!(err instanceof ApiError) || err.status !== 409) return false;
  const t = (err.body as { type?: string } | null)?.type;
  return t == null || VERSION_MISMATCH_TYPES.has(t);
}

/**
 * Format an ApiError body into a human-readable string for toast/snackbar.
 *
 * Backend always emits RFC 9457 problem+json (`{ type, title, status, detail }`)
 * for failures — see `ProblemDetails.fs`. 409 Conflict gets a friendlier
 * fixed message because the action was racy and the page just refetched.
 */
export function describeApiError(err: unknown): string {
  if (err instanceof ApiError) {
    if (isVersionMismatch(err)) {
      return "他の人が更新しました。再表示しましたので、再試行してください。";
    }
    const body = err.body as { detail?: string; title?: string } | null;
    if (body?.detail) return `${err.status}: ${body.detail}`;
    if (body?.title) return `${err.status}: ${body.title}`;
    return `API ${err.status} ${err.path}`;
  }
  return err instanceof Error ? err.message : String(err);
}
