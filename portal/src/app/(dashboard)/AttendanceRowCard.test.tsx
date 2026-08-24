import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { AttendanceRowCard } from "./AttendanceRowCard";
import type { AttendanceRowResponse } from "@/lib/types";

const baseRow: AttendanceRowResponse = {
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
};

describe("AttendanceRowCard", () => {
  it("renders the employee name, in/out times, shift, and worked hours", () => {
    render(<AttendanceRowCard row={baseRow} shiftName="Day Shift" showDate={false} />);

    expect(screen.getByText("On Time Otto")).toBeInTheDocument();
    expect(screen.getByText(/08:00/)).toBeInTheDocument();
    expect(screen.getByText(/16:00/)).toBeInTheDocument();
    expect(screen.getByText(/Day Shift/)).toBeInTheDocument();
    expect(screen.getByText(/8\.00h/)).toBeInTheDocument();
  });

  it("shows the date only when showDate is true", () => {
    const { rerender } = render(<AttendanceRowCard row={baseRow} shiftName="Day Shift" showDate={false} />);
    expect(screen.queryByText("2026-01-15")).not.toBeInTheDocument();

    rerender(<AttendanceRowCard row={baseRow} shiftName="Day Shift" showDate={true} />);
    expect(screen.getByText("2026-01-15")).toBeInTheDocument();
  });

  it("renders only the badges that are true", () => {
    const lateRow: AttendanceRowResponse = {
      ...baseRow,
      lastOut: null,
      workedHours: null,
      isLate: true,
      lateMinutes: 75,
      isMissingCheckout: true,
    };

    render(<AttendanceRowCard row={lateRow} shiftName="Day Shift" showDate={false} />);

    expect(screen.getByText("Late (75m)")).toBeInTheDocument();
    expect(screen.getByText("Missing Checkout")).toBeInTheDocument();
    expect(screen.queryByText("Double Punch")).not.toBeInTheDocument();
  });

  it("renders dashes for null first-in/last-out", () => {
    const absentRow: AttendanceRowResponse = { ...baseRow, firstIn: null, lastOut: null, workedHours: null };

    render(<AttendanceRowCard row={absentRow} shiftName="Day Shift" showDate={false} />);

    expect(screen.getByText("— – —")).toBeInTheDocument();
  });
});
