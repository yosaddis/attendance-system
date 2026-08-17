"use client";

import { useActionState } from "react";
import { createShift, type CreateShiftState } from "./actions";

export function ShiftForm() {
  const [state, formAction, isPending] = useActionState<CreateShiftState, FormData>(
    createShift,
    null,
  );

  return (
    <form action={formAction} className="grid grid-cols-2 gap-2 sm:grid-cols-3 md:grid-cols-6 items-end">
      {state?.error && (
        <p role="alert" className="col-span-full text-red-600 text-sm">
          {state.error}
        </p>
      )}
      <label className="flex flex-col text-sm gap-1">
        Name
        <input name="name" required className="border rounded px-2 py-1" />
      </label>
      <label className="flex flex-col text-sm gap-1">
        Start
        <input type="time" step={1} name="startTime" required className="border rounded px-2 py-1" />
      </label>
      <label className="flex flex-col text-sm gap-1">
        End
        <input type="time" step={1} name="endTime" required className="border rounded px-2 py-1" />
      </label>
      <label className="flex flex-col text-sm gap-1">
        Grace (min)
        <input type="number" name="graceMinutes" defaultValue={0} className="border rounded px-2 py-1" />
      </label>
      <label className="flex flex-col text-sm gap-1">
        Punch mode
        <select name="punchMode" className="border rounded px-2 py-1">
          <option value="TwoPunch">2-punch (IN/OUT)</option>
          <option value="FourPunch">4-punch (+ breaks)</option>
        </select>
      </label>
      <button type="submit" disabled={isPending} className="bg-blue-600 text-white rounded px-3 py-1">
        Add shift
      </button>
    </form>
  );
}
