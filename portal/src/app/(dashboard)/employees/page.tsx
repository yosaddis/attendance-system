import { backendFetch } from "@/lib/backendFetch";
import { getToken } from "@/lib/session";
import type { EmployeeResponse, ShiftResponse } from "@/lib/types";
import { EmployeeForm } from "./EmployeeForm";
import { deleteEmployee } from "./actions";

export default async function EmployeesPage() {
  const token = await getToken();
  const [employees, shifts]: [EmployeeResponse[], ShiftResponse[]] = await Promise.all([
    backendFetch("/api/employees", { token }),
    backendFetch("/api/shifts", { token }),
  ]);

  return (
    <div className="p-6 space-y-6">
      <h1 className="font-display text-2xl text-ink">Employees</h1>
      <EmployeeForm shifts={shifts} />
      <div className="hidden md:block overflow-x-auto bg-surface border border-border rounded-lg">
        <table className="w-full text-sm border-collapse">
          <thead>
            <tr className="text-left bg-surface-muted text-ink">
              <th className="py-3 px-4 font-medium">Code</th>
              <th className="py-3 px-4 font-medium">Name</th>
              <th className="py-3 px-4 font-medium">Shift</th>
              <th className="py-3 px-4"></th>
            </tr>
          </thead>
          <tbody>
            {employees.map((employee) => (
              <tr key={employee.id} className="border-b border-border last:border-b-0 hover:bg-surface-muted">
                <td className="py-3 px-4">{employee.employeeCode}</td>
                <td className="py-3 px-4">{employee.name}</td>
                <td className="py-3 px-4">{shifts.find((s) => s.id === employee.shiftId)?.name ?? "—"}</td>
                <td className="py-3 px-4">
                  <form action={deleteEmployee.bind(null, employee.id)}>
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
      <div
        data-testid="mobile-cards"
        className="md:hidden divide-y divide-border bg-surface border border-border rounded-lg"
      >
        {employees.map((employee) => (
          <div key={employee.id} className="p-4 flex items-center justify-between gap-3">
            <div>
              <div className="font-medium text-ink">{employee.name}</div>
              <div className="text-xs text-ink-soft mt-1">
                {employee.employeeCode} · {shifts.find((s) => s.id === employee.shiftId)?.name ?? "—"}
              </div>
            </div>
            <form action={deleteEmployee.bind(null, employee.id)}>
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
