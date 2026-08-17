import { describe, it, expect, vi } from "vitest";
import { backendFetch, BackendError } from "./backendFetch";

describe("backendFetch", () => {
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
});
