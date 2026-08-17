import { describe, it, expect, vi, beforeEach } from "vitest";

const cookieStore = vi.hoisted(() => {
  const store = new Map<string, string>();
  return {
    get: (name: string) => (store.has(name) ? { name, value: store.get(name)! } : undefined),
    set: (name: string, value: string) => {
      store.set(name, value);
    },
    _store: store,
  };
});

vi.mock("next/headers", () => ({ cookies: () => Promise.resolve(cookieStore) }));
vi.mock("next/navigation", () => ({ redirect: vi.fn() }));

import { redirect } from "next/navigation";
import { SESSION_COOKIE } from "@/lib/constants";
import { login } from "./actions";

describe("login action", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    cookieStore._store.clear();
  });

  it("sets the session cookie and redirects to /attendance on success", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        new Response(JSON.stringify({ token: "jwt-abc", role: "TenantAdmin", tenantId: "t1" }), { status: 200 }),
      ),
    );
    const formData = new FormData();
    formData.set("email", "admin@acme.test");
    formData.set("password", "correct-horse");

    await login(formData);

    expect(cookieStore.get(SESSION_COOKIE)?.value).toBe("jwt-abc");
    expect(redirect).toHaveBeenCalledWith("/attendance");
  });

  it("redirects to /login?error=invalid without setting a cookie on invalid credentials", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response("nope", { status: 401 })));
    const formData = new FormData();
    formData.set("email", "admin@acme.test");
    formData.set("password", "wrong");

    await login(formData);

    expect(cookieStore.get(SESSION_COOKIE)).toBeUndefined();
    expect(redirect).toHaveBeenCalledWith("/login?error=invalid");
  });

  it("redirects to /login?error=unreachable without setting a cookie when the backend can't be reached", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockRejectedValue(new TypeError("fetch failed")),
    );
    const formData = new FormData();
    formData.set("email", "admin@acme.test");
    formData.set("password", "correct-horse");

    await login(formData);

    expect(cookieStore.get(SESSION_COOKIE)).toBeUndefined();
    expect(redirect).toHaveBeenCalledWith("/login?error=unreachable");
  });

  it("redirects to /login?error=unreachable without setting a cookie on a backend 500", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response("boom", { status: 500 })));
    const formData = new FormData();
    formData.set("email", "admin@acme.test");
    formData.set("password", "correct-horse");

    await login(formData);

    expect(cookieStore.get(SESSION_COOKIE)).toBeUndefined();
    expect(redirect).toHaveBeenCalledWith("/login?error=unreachable");
  });

  it("redirects to /login?error=role without setting a cookie when the user is not a TenantAdmin", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        new Response(JSON.stringify({ token: "jwt-abc", role: "Operator", tenantId: "t1" }), { status: 200 }),
      ),
    );
    const formData = new FormData();
    formData.set("email", "operator@acme.test");
    formData.set("password", "correct-horse");

    await login(formData);

    expect(cookieStore.get(SESSION_COOKIE)).toBeUndefined();
    expect(redirect).toHaveBeenCalledWith("/login?error=role");
  });
});
