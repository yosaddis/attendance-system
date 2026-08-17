import { backendFetch } from "@/lib/backendFetch";
import { getToken } from "@/lib/session";
import type { DailyAttendanceResponse } from "@/lib/types";
import { DateNav } from "./DateNav";

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
  const rows: DailyAttendanceResponse[] = await backendFetch(`/api/attendance/daily?date=${date}`, {
    token: await getToken(),
  });

  return (
    <div className="p-4 space-y-6">
      <h1 className="text-xl font-semibold">Daily Attendance</h1>
      <DateNav date={date} />
      <div className="overflow-x-auto">
        <table className="w-full text-sm border-collapse">
          <thead>
            <tr className="text-left border-b">
              <th className="py-2">Employee</th>
              <th>First In</th>
              <th>Last Out</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.employeeId} className="border-b">
                <td className="py-2">{row.employeeName}</td>
                <td>{row.firstIn ? new Date(row.firstIn).toLocaleTimeString() : "—"}</td>
                <td>{row.lastOut ? new Date(row.lastOut).toLocaleTimeString() : "—"}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
