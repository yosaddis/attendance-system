import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, within } from "@testing-library/react";

vi.mock("@/lib/backendFetch", () => ({ backendFetch: vi.fn() }));
vi.mock("@/lib/session", () => ({ getToken: vi.fn().mockResolvedValue("jwt-abc") }));
vi.mock("next/navigation", () => ({ useRouter: () => ({ push: vi.fn() }) }));

import { backendFetch } from "@/lib/backendFetch";
import AttendancePage from "./page";

const GMT_PLUS_3_MS = 3 * 60 * 60 * 1000;

function todayIsoDate(): string {
  return new Date(Date.now() + GMT_PLUS_3_MS).toISOString().slice(0, 10);
}

describe("AttendancePage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(backendFetch).mockResolvedValue([]);
  });

  it("falls back to today's date instead of crashing when the date search param is malformed", async () => {
    await AttendancePage({ searchParams: Promise.resolve({ date: "notadate" }) });

    expect(backendFetch).toHaveBeenCalledWith(
      `/api/attendance/daily?date=${todayIsoDate()}`,
      expect.anything(),
    );
  });

  it("falls back to today's date when the date search param is absent", async () => {
    await AttendancePage({ searchParams: Promise.resolve({}) });

    expect(backendFetch).toHaveBeenCalledWith(
      `/api/attendance/daily?date=${todayIsoDate()}`,
      expect.anything(),
    );
  });

  it("uses the requested date when it matches YYYY-MM-DD", async () => {
    await AttendancePage({ searchParams: Promise.resolve({ date: "2026-01-15" }) });

    expect(backendFetch).toHaveBeenCalledWith("/api/attendance/daily?date=2026-01-15", expect.anything());
  });

  it("falls back to today's date when the date search param is shape-valid but calendrically impossible", async () => {
    await AttendancePage({ searchParams: Promise.resolve({ date: "9999-99-99" }) });

    expect(backendFetch).toHaveBeenCalledWith(
      `/api/attendance/daily?date=${todayIsoDate()}`,
      expect.anything(),
    );
  });

  it("falls back to today's date when the date search param overflows into a different calendar date", async () => {
    await AttendancePage({ searchParams: Promise.resolve({ date: "2026-02-30" }) });

    expect(backendFetch).toHaveBeenCalledWith(
      `/api/attendance/daily?date=${todayIsoDate()}`,
      expect.anything(),
    );
  });

  it("renders worked hours and only the badges that apply in the desktop table", async () => {
    vi.mocked(backendFetch).mockResolvedValue([
      {
        employeeId: "e1",
        employeeName: "On Time Otto",
        date: "2026-01-15",
        shiftId: "s1",
        firstIn: "2026-01-15T05:00:00Z",
        lastOut: "2026-01-15T13:00:00Z",
        workedHours: 8,
        hasShift: true,
        isLate: false,
        lateMinutes: null,
        isMissingCheckout: false,
        hasDoublePunch: false,
      },
      {
        employeeId: "e2",
        employeeName: "Late Larry",
        date: "2026-01-15",
        shiftId: "s1",
        firstIn: "2026-01-15T07:00:00Z",
        lastOut: null,
        workedHours: null,
        hasShift: true,
        isLate: true,
        lateMinutes: 75,
        isMissingCheckout: true,
        hasDoublePunch: false,
      },
    ]);

    render(await AttendancePage({ searchParams: Promise.resolve({ date: "2026-01-15" }) }));

    const table = screen.getByRole("table");
    expect(within(table).getByText("8.00")).toBeInTheDocument();
    expect(within(table).getByText("Late (75m)")).toBeInTheDocument();
    expect(within(table).getByText(/missing checkout/i)).toBeInTheDocument();
    expect(within(table).queryByText(/double punch/i)).not.toBeInTheDocument();
  });

  it("renders the same rows in the mobile card list", async () => {
    vi.mocked(backendFetch).mockResolvedValue([
      {
        employeeId: "e1",
        employeeName: "On Time Otto",
        date: "2026-01-15",
        shiftId: "s1",
        firstIn: "2026-01-15T05:00:00Z",
        lastOut: "2026-01-15T13:00:00Z",
        workedHours: 8,
        hasShift: true,
        isLate: false,
        lateMinutes: null,
        isMissingCheckout: false,
        hasDoublePunch: false,
      },
      {
        employeeId: "e2",
        employeeName: "Late Larry",
        date: "2026-01-15",
        shiftId: "s1",
        firstIn: "2026-01-15T07:00:00Z",
        lastOut: null,
        workedHours: null,
        hasShift: true,
        isLate: true,
        lateMinutes: 75,
        isMissingCheckout: true,
        hasDoublePunch: false,
      },
    ]);

    render(await AttendancePage({ searchParams: Promise.resolve({ date: "2026-01-15" }) }));

    const cards = screen.getByTestId("mobile-cards");
    expect(within(cards).getByText("On Time Otto")).toBeInTheDocument();
    expect(within(cards).getByText("Late Larry")).toBeInTheDocument();
    expect(within(cards).getByText(/8\.00h/)).toBeInTheDocument();
    expect(within(cards).getByText("Late (75m)")).toBeInTheDocument();
    expect(within(cards).getByText(/missing checkout/i)).toBeInTheDocument();
    expect(screen.getByTestId("mobile-cards").className).toContain("md:hidden");
    expect(screen.getByRole("table").parentElement!.className).toContain("hidden md:block");
  });
});
