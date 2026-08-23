import { backendFetch } from "@/lib/backendFetch";
import { getToken } from "@/lib/session";
import type { AttendanceRowResponse, ShiftResponse } from "@/lib/types";
import { DateNav } from "./DateNav";
import { Badge } from "../Badge";

const ISO_DATE_PATTERN = /^\d{4}-\d{2}-\d{2}$/;

function todayIsoDate(): string {
  return new Date().toISOString().slice(0, 10);
}

function isValidIsoDate(candidate: string): boolean {
  const asDate = new Date(`${candidate}T00:00:00Z`);
  if (Number.isNaN(asDate.getTime())) return false;
  return asDate.toISOString().slice(0, 10) === candidate;
}

export default async function AttendancePage({
  searchParams,
}: {
  searchParams: Promise<{ date?: string }>;
}) {
  const { date: requestedDate } = await searchParams;
  const date =
    requestedDate && ISO_DATE_PATTERN.test(requestedDate) && isValidIsoDate(requestedDate)
      ? requestedDate
      : todayIsoDate();
  const token = await getToken();
  const [rows, shifts]: [AttendanceRowResponse[], ShiftResponse[]] = await Promise.all([
    backendFetch(`/api/attendance/daily?date=${date}`, { token }),
    backendFetch("/api/shifts", { token }),
  ]);

  return (
    <div className="p-6 space-y-6">
      <h1 className="font-display text-2xl text-ink">Daily Attendance</h1>
      <DateNav date={date} />
      <div className="overflow-x-auto bg-surface border border-border rounded-lg">
        <table className="w-full text-sm border-collapse">
          <thead>
            <tr className="text-left bg-surface-muted text-ink">
              <th className="py-3 px-4 font-medium">Employee</th>
              <th className="py-3 px-4 font-medium">Shift</th>
              <th className="py-3 px-4 font-medium">First In</th>
              <th className="py-3 px-4 font-medium">Last Out</th>
              <th className="py-3 px-4 font-medium">Worked Hours</th>
              <th className="py-3 px-4 font-medium">Flags</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.employeeId} className="border-b border-border last:border-b-0 hover:bg-surface-muted">
                <td className="py-3 px-4">{row.employeeName}</td>
                <td className="py-3 px-4">{shifts.find((s) => s.id === row.shiftId)?.name ?? "—"}</td>
                <td className="py-3 px-4">{row.firstIn ? new Date(row.firstIn).toLocaleTimeString() : "—"}</td>
                <td className="py-3 px-4">{row.lastOut ? new Date(row.lastOut).toLocaleTimeString() : "—"}</td>
                <td className="py-3 px-4">{row.workedHours !== null ? row.workedHours.toFixed(2) : "—"}</td>
                <td className="py-3 px-4">
                  {row.isLate && <Badge>Late{row.lateMinutes !== null ? ` (${row.lateMinutes}m)` : ""}</Badge>}
                  {row.isMissingCheckout && <Badge>Missing Checkout</Badge>}
                  {row.hasDoublePunch && <Badge>Double Punch</Badge>}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
