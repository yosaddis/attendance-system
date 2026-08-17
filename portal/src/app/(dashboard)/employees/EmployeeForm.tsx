"use client";

import type { ShiftResponse } from "@/lib/types";
import { createEmployee } from "./actions";

export function EmployeeForm({ shifts }: { shifts: ShiftResponse[] }) {
  return (
    <form action={createEmployee} className="grid grid-cols-2 gap-2 sm:grid-cols-4 items-end">
      <label htmlFor="employeeCode" className="flex flex-col text-sm gap-1">
        Employee code
        <input id="employeeCode" name="employeeCode" required className="border rounded px-2 py-1" />
      </label>
      <label htmlFor="name" className="flex flex-col text-sm gap-1">
        Name
        <input id="name" name="name" required className="border rounded px-2 py-1" />
      </label>
      <label htmlFor="shiftId" className="flex flex-col text-sm gap-1">
        Shift
        <select id="shiftId" name="shiftId" className="border rounded px-2 py-1">
          <option value="">— None —</option>
          {shifts.map((shift) => (
            <option key={shift.id} value={shift.id}>
              {shift.name}
            </option>
          ))}
        </select>
      </label>
      <button type="submit" className="bg-blue-600 text-white rounded px-3 py-1">
        Add employee
      </button>
    </form>
  );
}
