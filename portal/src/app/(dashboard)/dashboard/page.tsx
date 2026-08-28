import { backendFetch } from "@/lib/backendFetch";
import { getToken } from "@/lib/session";
import { todayIsoDateTenant } from "@/lib/tenantTime";
import type { AttendanceSummaryResponse } from "@/lib/types";

export default async function DashboardPage() {
  const date = todayIsoDateTenant();
  const summary: AttendanceSummaryResponse = await backendFetch(`/api/attendance/summary?date=${date}`, {
    token: await getToken(),
  });

  const cards = [
    { label: "Present", value: summary.presentCount },
    { label: "Absent", value: summary.absentCount },
    { label: "Late", value: summary.lateCount },
    { label: "Total (with shift)", value: summary.totalEmployeesWithShift },
  ];

  return (
    <div className="p-6 space-y-6">
      <h1 className="font-display text-2xl text-ink">Dashboard</h1>
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
        {cards.map((card) => (
          <div key={card.label} className="bg-surface border border-border rounded-lg p-5">
            <div className="text-sm text-ink-soft">{card.label}</div>
            <div className="font-display text-3xl text-ink mt-1">{card.value}</div>
          </div>
        ))}
      </div>
    </div>
  );
}
