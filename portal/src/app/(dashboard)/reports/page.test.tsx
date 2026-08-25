import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, within } from "@testing-library/react";

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

  it("renders row data in both the desktop table and the mobile card list", async () => {
    vi.mocked(backendFetch).mockImplementation((path: string) => {
      if (path.startsWith("/api/shifts")) return Promise.resolve([]);
      return Promise.resolve([
        {
          employeeId: "e1",
          employeeName: "Range Rita",
          date: "2026-01-01",
          shiftId: "s1",
          firstIn: "2026-01-01T05:00:00Z",
          lastOut: "2026-01-01T13:00:00Z",
          workedHours: 8,
          hasShift: true,
          isLate: false,
          lateMinutes: null,
          isMissingCheckout: false,
          hasDoublePunch: false,
        },
      ]);
    });

    render(await ReportsPage({ searchParams: Promise.resolve({ from: "2026-01-01", to: "2026-01-01" }) }));

    const table = screen.getByRole("table");
    expect(within(table).getByText("Range Rita")).toBeInTheDocument();
    expect(within(table).getByText("8.00")).toBeInTheDocument();

    const cards = screen.getByTestId("mobile-cards");
    expect(within(cards).getByText("Range Rita")).toBeInTheDocument();
    expect(within(cards).getByText("2026-01-01")).toBeInTheDocument();
    expect(within(cards).getByText(/8\.00h/)).toBeInTheDocument();
    expect(screen.getByTestId("mobile-cards").className).toContain("md:hidden");
    expect(screen.getByRole("table").parentElement!.className).toContain("hidden md:block");
  });
});
