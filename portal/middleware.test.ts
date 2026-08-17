import { describe, it, expect } from "vitest";
import { NextRequest } from "next/server";
import { middleware } from "./middleware";

describe("middleware", () => {
  it("redirects unauthenticated requests to protected paths to /login", () => {
    const request = new NextRequest("http://localhost/employees");

    const response = middleware(request);

    expect(response.status).toBe(307);
    expect(response.headers.get("location")).toBe("http://localhost/login");
  });

  it("passes through authenticated requests to protected paths", () => {
    const request = new NextRequest("http://localhost/employees", {
      headers: { Cookie: "zak_session=jwt-abc" },
    });

    const response = middleware(request);

    expect(response.status).toBe(200);
  });

  it("passes through unprotected paths without a session", () => {
    const request = new NextRequest("http://localhost/login");

    const response = middleware(request);

    expect(response.status).toBe(200);
  });
});
