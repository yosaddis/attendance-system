import { backendFetch } from "@/lib/backendFetch";
import { getToken } from "@/lib/session";
import type { AttendanceRowResponse, ShiftResponse } from "@/lib/types";
import { DateRangeNav } from "./DateRangeNav";

const ISO_DATE_PATTERN = /^\d{4}-\d{2}-\d{2}$/;
const GMT_PLUS_3_MS = 3 * 60 * 60 * 1000;

function todayIsoDateGmtPlus3(): string {
  return new Date(Date.now() + GMT_PLUS_3_MS).toISOString().slice(0, 10);
}

function isValidIsoDate(candidate: string): boolean {
  const asDate = new Date(`${candidate}T00:00:00Z`);
  if (Number.isNaN(asDate.getTime())) return false;
  return asDate.toISOString().slice(0, 10) === candidate;
}

function defaultRange(): { from: string; to: string } {
  const to = todayIsoDateGmtPlus3();
  const from = new Date(new Date(`${to}T00:00:00Z`).getTime() - 6 * 24 * 60 * 60 * 1000)
    .toISOString()
    .slice(0, 10);
  return { from, to };
}

export default async function ReportsPage({
  searchParams,
}: {
  searchParams: Promise<{ from?: string; to?: string }>;
}) {
  const { from: requestedFrom, to: requestedTo } = await searchParams;
  const fallback = defaultRange();
  const from =
    requestedFrom && ISO_DATE_PATTERN.test(requestedFrom) && isValidIsoDate(requestedFrom)
      ? requestedFrom
      : fallback.from;
  const to =
    requestedTo && ISO_DATE_PATTERN.test(requestedTo) && isValidIsoDate(requestedTo)
      ? requestedTo
      : fallback.to;

  const token = await getToken();
  const [rows, shifts]: [AttendanceRowResponse[], ShiftResponse[]] = await Promise.all([
    backendFetch(`/api/attendance/report?from=${from}&to=${to}`, { token }),
    backendFetch("/api/shifts", { token }),
  ]);

  return (
    <div className="p-6 space-y-6">
      <h1 className="font-display text-2xl text-ink">Reports</h1>
      <div className="flex flex-wrap items-end justify-between gap-4">
        <DateRangeNav from={from} to={to} />
        <a
          href={`/reports/export?from=${from}&to=${to}`}
          className="border border-border rounded-md px-4 py-2 text-ink hover:border-accent"
        >
          Export CSV
        </a>
      </div>
      <div className="overflow-x-auto bg-surface border border-border rounded-lg">
        <table className="w-full text-sm border-collapse">
          <thead>
            <tr className="text-left bg-surface-muted text-ink">
              <th className="py-3 px-4 font-medium">Employee</th>
              <th className="py-3 px-4 font-medium">Date</th>
              <th className="py-3 px-4 font-medium">Shift</th>
              <th className="py-3 px-4 font-medium">First In</th>
              <th className="py-3 px-4 font-medium">Last Out</th>
              <th className="py-3 px-4 font-medium">Worked Hours</th>
              <th className="py-3 px-4 font-medium">Flags</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={`${row.employeeId}-${row.date}`} className="border-b border-border last:border-b-0 hover:bg-surface-muted">
                <td className="py-3 px-4">{row.employeeName}</td>
                <td className="py-3 px-4">{row.date}</td>
                <td className="py-3 px-4">{shifts.find((s) => s.id === row.shiftId)?.name ?? "—"}</td>
                <td className="py-3 px-4">{row.firstIn ? new Date(row.firstIn).toLocaleTimeString() : "—"}</td>
                <td className="py-3 px-4">{row.lastOut ? new Date(row.lastOut).toLocaleTimeString() : "—"}</td>
                <td className="py-3 px-4">{row.workedHours !== null ? row.workedHours.toFixed(2) : "—"}</td>
                <td className="py-3 px-4">
                  {row.isLate && <span className="text-danger text-xs mr-1">Late</span>}
                  {row.isMissingCheckout && <span className="text-danger text-xs mr-1">Missing Checkout</span>}
                  {row.hasDoublePunch && <span className="text-danger text-xs mr-1">Double Punch</span>}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
