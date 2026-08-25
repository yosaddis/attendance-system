import { backendFetch } from "@/lib/backendFetch";
import { getToken } from "@/lib/session";
import type { ShiftResponse } from "@/lib/types";
import { ShiftForm } from "./ShiftForm";
import { deleteShift } from "./actions";

export default async function ShiftsPage() {
  const shifts: ShiftResponse[] = await backendFetch("/api/shifts", { token: await getToken() });

  return (
    <div className="p-6 space-y-6">
      <h1 className="font-display text-2xl text-ink">Shifts</h1>
      <ShiftForm />
      <div className="hidden md:block overflow-x-auto bg-surface border border-border rounded-lg">
        <table className="w-full text-sm border-collapse">
          <thead>
            <tr className="text-left bg-surface-muted text-ink">
              <th className="py-3 px-4 font-medium">Name</th>
              <th className="py-3 px-4 font-medium">Start</th>
              <th className="py-3 px-4 font-medium">End</th>
              <th className="py-3 px-4 font-medium">Grace (min)</th>
              <th className="py-3 px-4 font-medium">Mode</th>
              <th className="py-3 px-4"></th>
            </tr>
          </thead>
          <tbody>
            {shifts.map((shift) => (
              <tr key={shift.id} className="border-b border-border last:border-b-0 hover:bg-surface-muted">
                <td className="py-3 px-4">{shift.name}</td>
                <td className="py-3 px-4">{shift.startTime}</td>
                <td className="py-3 px-4">{shift.endTime}</td>
                <td className="py-3 px-4">{shift.graceMinutes}</td>
                <td className="py-3 px-4">{shift.punchMode}</td>
                <td className="py-3 px-4">
                  <form action={deleteShift.bind(null, shift.id)}>
                    <button type="submit" className="text-danger hover:underline text-sm">
                      Delete
                    </button>
                  </form>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <div className="md:hidden divide-y divide-border bg-surface border border-border rounded-lg">
        {shifts.map((shift) => (
          <div key={shift.id} className="p-4 flex items-center justify-between gap-3">
            <div>
              <div className="font-medium text-ink">{shift.name}</div>
              <div className="text-xs text-ink-soft mt-1">
                {shift.startTime}–{shift.endTime} · {shift.graceMinutes}min grace · {shift.punchMode}
              </div>
            </div>
            <form action={deleteShift.bind(null, shift.id)}>
              <button type="submit" className="text-danger text-sm shrink-0">
                Delete
              </button>
            </form>
          </div>
        ))}
      </div>
    </div>
  );
}
