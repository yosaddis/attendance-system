import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";

vi.mock("@/lib/backendFetch", () => ({ backendFetch: vi.fn() }));
vi.mock("@/lib/session", () => ({ getToken: vi.fn().mockResolvedValue("jwt-abc") }));

import { backendFetch } from "@/lib/backendFetch";
import DashboardPage from "./page";

describe("DashboardPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(backendFetch).mockResolvedValue({
      totalEmployeesWithShift: 10,
      presentCount: 7,
      absentCount: 2,
      lateCount: 1,
    });
  });

  it("fetches today's summary (GMT+3) and renders the four KPI cards", async () => {
    render(await DashboardPage());

    expect(backendFetch).toHaveBeenCalledWith(
      expect.stringMatching(/^\/api\/attendance\/summary\?date=\d{4}-\d{2}-\d{2}$/),
      expect.anything(),
    );
    expect(screen.getByText("7")).toBeInTheDocument();
    expect(screen.getByText("2")).toBeInTheDocument();
    expect(screen.getByText("1")).toBeInTheDocument();
    expect(screen.getByText("10")).toBeInTheDocument();
    expect(screen.getByText("Present")).toBeInTheDocument();
    expect(screen.getByText("Absent")).toBeInTheDocument();
    expect(screen.getByText("Late")).toBeInTheDocument();
  });
});
