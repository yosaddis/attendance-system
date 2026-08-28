import { describe, it, expect, vi, beforeEach } from "vitest";

const { redirectMock } = vi.hoisted(() => ({
  redirectMock: vi.fn((url: string) => {
    throw new Error(`REDIRECT:${url}`);
  }),
}));

vi.mock("next/navigation", () => ({ redirect: redirectMock }));

import { backendFetch, BackendError } from "./backendFetch";

describe("backendFetch", () => {
  beforeEach(() => {
    redirectMock.mockClear();
  });

  it("attaches an Authorization header when a token is provided", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ ok: true }), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    await backendFetch("/api/employees", { token: "jwt-abc" });

    const [, init] = fetchMock.mock.calls[0];
    expect((init.headers as Headers).get("Authorization")).toBe("Bearer jwt-abc");
  });

  it("throws BackendError carrying the response status on failure", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response("nope", { status: 403 })));

    await expect(backendFetch("/api/employees")).rejects.toBeInstanceOf(BackendError);
  });

  it("returns null for a 204 No Content response", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(null, { status: 204 })));

    const result = await backendFetch("/api/employees/123", { method: "DELETE", token: "jwt-abc" });

    expect(result).toBeNull();
  });

  it("redirects to /login on a 401 for an authenticated request", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response("expired", { status: 401 })));

    await expect(backendFetch("/api/employees", { token: "stale-jwt" })).rejects.toThrow("REDIRECT:/login");

    expect(redirectMock).toHaveBeenCalledWith("/login");
  });

  it("does not auto-redirect on a 401 for an unauthenticated request (e.g. a failed login)", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response("bad credentials", { status: 401 })));

    await expect(backendFetch("/api/auth/login")).rejects.toBeInstanceOf(BackendError);

    expect(redirectMock).not.toHaveBeenCalled();
  });
});
