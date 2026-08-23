import { describe, it, expect, vi, beforeEach } from "vitest";
import { NextRequest } from "next/server";

vi.mock("@/lib/session", () => ({ getToken: vi.fn() }));

import { getToken } from "@/lib/session";
import { GET } from "./route";

describe("GET /reports/export", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("sends the session token to the backend as an Authorization: Bearer header", async () => {
    vi.mocked(getToken).mockResolvedValue("jwt-secret-token");
    const fetchMock = vi.fn().mockResolvedValue(
      new Response("employee,date\n", {
        status: 200,
        headers: { "Content-Disposition": "attachment; filename=report.csv" },
      }),
    );
    vi.stubGlobal("fetch", fetchMock);

    const request = new NextRequest("http://localhost/reports/export?from=2026-01-01&to=2026-01-07");
    await GET(request);

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [, init] = fetchMock.mock.calls[0];
    expect((init.headers as Record<string, string>).Authorization).toBe("Bearer jwt-secret-token");
  });

  it("never leaks the session token back to the caller in the response headers", async () => {
    vi.mocked(getToken).mockResolvedValue("jwt-secret-token");
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        new Response("employee,date\n", {
          status: 200,
          headers: { "Content-Disposition": "attachment; filename=report.csv" },
        }),
      ),
    );

    const request = new NextRequest("http://localhost/reports/export?from=2026-01-01&to=2026-01-07");
    const response = await GET(request);

    const headerNames = [...response.headers.keys()].map((name) => name.toLowerCase());
    expect(headerNames.sort()).toEqual(["content-disposition", "content-type"]);
    expect(response.headers.get("Authorization")).toBeNull();
    expect(response.headers.get("Content-Type")).toBe("text/csv");
    expect(response.headers.get("Content-Disposition")).toBe("attachment; filename=report.csv");
    for (const value of response.headers.values()) {
      expect(value).not.toContain("jwt-secret-token");
    }
  });
});
