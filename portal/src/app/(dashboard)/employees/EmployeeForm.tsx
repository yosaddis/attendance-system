"use client";

import Link from "next/link";
import { useActionState } from "react";
import type { EmployeeResponse, ShiftResponse } from "@/lib/types";
import { createEmployee, updateEmployee, type CreateEmployeeState } from "./actions";

export function EmployeeForm({ shifts, employee }: { shifts: ShiftResponse[]; employee?: EmployeeResponse }) {
  const action = employee ? updateEmployee.bind(null, employee.id) : createEmployee;
  const [state, formAction, isPending] = useActionState<CreateEmployeeState, FormData>(action, null);

  return (
    <form
      action={formAction}
      className="grid grid-cols-2 gap-3 sm:grid-cols-4 items-end bg-surface border border-border rounded-lg p-4"
    >
      {state?.error && (
        <p role="alert" className="col-span-full bg-danger-bg text-danger text-sm rounded-md px-3 py-2 border border-danger/20">
          {state.error}
        </p>
      )}
      <label htmlFor="employeeCode" className="flex flex-col text-sm gap-1 text-ink-soft">
        Employee code
        <input
          id="employeeCode"
          name="employeeCode"
          required
          readOnly={!!employee}
          defaultValue={employee?.employeeCode}
          className={
            "border border-border rounded-md px-3 py-2 text-ink focus:outline-none " +
            (employee
              ? "bg-surface-muted text-ink-soft cursor-not-allowed"
              : "focus:ring-2 focus:ring-accent focus:border-accent")
          }
        />
      </label>
      <label htmlFor="name" className="flex flex-col text-sm gap-1 text-ink-soft">
        Name
        <input
          id="name"
          name="name"
          required
          defaultValue={employee?.name}
          className="border border-border rounded-md px-3 py-2 text-ink focus:outline-none focus:ring-2 focus:ring-accent focus:border-accent"
        />
      </label>
      <label htmlFor="shiftId" className="flex flex-col text-sm gap-1 text-ink-soft">
        Shift
        <select
          id="shiftId"
          name="shiftId"
          defaultValue={employee?.shiftId ?? ""}
          className="border border-border rounded-md px-3 py-2 text-ink focus:outline-none focus:ring-2 focus:ring-accent focus:border-accent"
        >
          <option value="">— None —</option>
          {shifts.map((shift) => (
            <option key={shift.id} value={shift.id}>
              {shift.name}
            </option>
          ))}
        </select>
      </label>
      <div className="flex gap-3">
        <button
          type="submit"
          disabled={isPending}
          className="bg-accent hover:bg-accent-hover text-accent-ink font-medium rounded-md px-4 py-2 transition-colors disabled:opacity-60"
        >
          {employee ? "Save changes" : "Add employee"}
        </button>
        {employee && (
          <Link
            href="/employees"
            className="border border-border rounded-md px-4 py-2 text-ink hover:border-accent flex items-center"
          >
            Cancel
          </Link>
        )}
      </div>
    </form>
  );
}
