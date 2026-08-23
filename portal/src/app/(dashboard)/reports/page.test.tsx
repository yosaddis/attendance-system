import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";

vi.mock("next/navigation", () => ({ useRouter: () => ({ push: vi.fn() }) }));
vi.mock("@/lib/backendFetch", () => ({ backendFetch: vi.fn() }));
vi.mock("@/lib/session", () => ({ getToken: vi.fn().mockResolvedValue("jwt-abc") }));

import { backendFetch } from "@/lib/backendFetch";
import ReportsPage from "./page";

describe("ReportsPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(backendFetch).mockImplementation((path: string) =>
      Promise.resolve(path.startsWith("/api/shifts") ? [] : []),
    );
  });

  it("defaults to the last 7 days when no range is given", async () => {
    await ReportsPage({ searchParams: Promise.resolve({}) });

    expect(backendFetch).toHaveBeenCalledWith(
      expect.stringMatching(/^\/api\/attendance\/report\?from=\d{4}-\d{2}-\d{2}&to=\d{4}-\d{2}-\d{2}$/),
      expect.anything(),
    );
  });

  it("uses the requested from/to when both are valid", async () => {
    await ReportsPage({ searchParams: Promise.resolve({ from: "2026-01-01", to: "2026-01-07" }) });

    expect(backendFetch).toHaveBeenCalledWith(
      "/api/attendance/report?from=2026-01-01&to=2026-01-07",
      expect.anything(),
    );
  });

  it("renders an export link pointing at the export route with the same range", async () => {
    render(await ReportsPage({ searchParams: Promise.resolve({ from: "2026-01-01", to: "2026-01-07" }) }));

    const link = screen.getByRole("link", { name: /export csv/i });
    expect(link).toHaveAttribute("href", "/reports/export?from=2026-01-01&to=2026-01-07");
  });
});
