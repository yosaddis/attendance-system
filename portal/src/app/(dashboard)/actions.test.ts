import { describe, it, expect, vi, beforeEach } from "vitest";

const cookieStore = vi.hoisted(() => {
  const store = new Map<string, string>();
  return {
    get: (name: string) => (store.has(name) ? { name, value: store.get(name)! } : undefined),
    set: (name: string, value: string) => {
      store.set(name, value);
    },
    delete: (name: string) => {
      store.delete(name);
    },
  };
});

vi.mock("next/headers", () => ({ cookies: () => cookieStore }));
vi.mock("next/navigation", () => ({ redirect: vi.fn() }));

import { redirect } from "next/navigation";
import { SESSION_COOKIE } from "@/lib/constants";
import { logout } from "./actions";

describe("logout action", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    cookieStore.set(SESSION_COOKIE, "jwt-abc");
  });

  it("clears the session cookie and redirects to /login", async () => {
    await logout();

    expect(cookieStore.get(SESSION_COOKIE)).toBeUndefined();
    expect(redirect).toHaveBeenCalledWith("/login");
  });
});
