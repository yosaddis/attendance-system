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
    <div className="p-4 space-y-6">
      <h1 className="text-xl font-semibold">Employees</h1>
      <EmployeeForm shifts={shifts} />
      <div className="overflow-x-auto">
        <table className="w-full text-sm border-collapse">
          <thead>
            <tr className="text-left border-b">
              <th className="py-2">Code</th>
              <th>Name</th>
              <th>Shift</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {employees.map((employee) => (
              <tr key={employee.id} className="border-b">
                <td className="py-2">{employee.employeeCode}</td>
                <td>{employee.name}</td>
                <td>{shifts.find((s) => s.id === employee.shiftId)?.name ?? "—"}</td>
                <td>
                  <form action={deleteEmployee.bind(null, employee.id)}>
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
