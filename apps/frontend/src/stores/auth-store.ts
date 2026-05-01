import { decodeJwt } from "jose";
import { create } from "zustand";

export type Role = "viewer" | "operator" | "admin";

const ROLE_RANK: Record<Role, number> = { viewer: 1, operator: 2, admin: 3 };

type AuthState = {
  token: string | null;
  roles: Set<string>;
  userId: string | null;
  setToken: (t: string | null) => void;
  clear: () => void;
  hasRole: (role: Role) => boolean;
};

function rolesFromToken(token: string | null): Set<string> {
  if (!token) return new Set();
  try {
    const payload = decodeJwt(token) as {
      realm_access?: { roles?: string[] };
    };
    return new Set(payload.realm_access?.roles ?? []);
  } catch {
    return new Set();
  }
}

function userIdFromToken(token: string | null): string | null {
  if (!token) return null;
  try {
    const payload = decodeJwt(token) as { sub?: string };
    return payload.sub ?? null;
  } catch {
    return null;
  }
}

const initialToken = (import.meta.env.VITE_DEV_TOKEN as string | undefined) || null;

export const useAuth = create<AuthState>((set, get) => ({
  token: initialToken,
  roles: rolesFromToken(initialToken),
  userId: userIdFromToken(initialToken),
  setToken: (t) => set({ token: t, roles: rolesFromToken(t), userId: userIdFromToken(t) }),
  clear: () => set({ token: null, roles: new Set(), userId: null }),
  hasRole: (role) => {
    const need = ROLE_RANK[role];
    const have = Array.from(get().roles).reduce((max, r) => {
      const rank = ROLE_RANK[r as Role];
      return rank && rank > max ? rank : max;
    }, 0);
    return have >= need;
  },
}));
