import { backendFetch } from "@/lib/backendFetch";
import { getToken } from "@/lib/session";
import type { ShiftResponse } from "@/lib/types";
import { ShiftForm } from "./ShiftForm";
import { deleteShift } from "./actions";

export default async function ShiftsPage() {
  const shifts: ShiftResponse[] = await backendFetch("/api/shifts", { token: await getToken() });

  return (
    <div className="p-4 space-y-6">
      <h1 className="text-xl font-semibold">Shifts</h1>
      <ShiftForm />
      <div className="overflow-x-auto">
        <table className="w-full text-sm border-collapse">
          <thead>
            <tr className="text-left border-b">
              <th className="py-2">Name</th>
              <th>Start</th>
              <th>End</th>
              <th>Grace (min)</th>
              <th>Mode</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {shifts.map((shift) => (
              <tr key={shift.id} className="border-b">
                <td className="py-2">{shift.name}</td>
                <td>{shift.startTime}</td>
                <td>{shift.endTime}</td>
                <td>{shift.graceMinutes}</td>
                <td>{shift.punchMode}</td>
                <td>
                  <form action={deleteShift.bind(null, shift.id)}>
                    <button type="submit" className="text-red-600">
                      Delete
                    </button>
                  </form>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
