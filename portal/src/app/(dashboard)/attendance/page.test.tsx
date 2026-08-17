import { describe, it, expect, vi, beforeEach } from "vitest";

vi.mock("@/lib/backendFetch", () => ({ backendFetch: vi.fn() }));
vi.mock("@/lib/session", () => ({ getToken: vi.fn().mockResolvedValue("jwt-abc") }));

import { backendFetch } from "@/lib/backendFetch";
import AttendancePage from "./page";

function todayIsoDate(): string {
  return new Date().toISOString().slice(0, 10);
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
});
