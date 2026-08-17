"use client";

import { useActionState } from "react";
import type { ShiftResponse } from "@/lib/types";
import { createEmployee, type CreateEmployeeState } from "./actions";

export function EmployeeForm({ shifts }: { shifts: ShiftResponse[] }) {
  const [state, formAction, isPending] = useActionState<CreateEmployeeState, FormData>(
    createEmployee,
    null,
  );

  return (
    <form action={formAction} className="grid grid-cols-2 gap-2 sm:grid-cols-4 items-end">
      {state?.error && (
        <p role="alert" className="col-span-full text-red-600 text-sm">
          {state.error}
        </p>
      )}
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
      <button type="submit" disabled={isPending} className="bg-blue-600 text-white rounded px-3 py-1">
        Add employee
      </button>
    </form>
  );
}
