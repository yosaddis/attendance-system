"use client";

import { useActionState } from "react";
import { createShift, type CreateShiftState } from "./actions";

export function ShiftForm() {
  const [state, formAction, isPending] = useActionState<CreateShiftState, FormData>(
    createShift,
    null,
  );

  const inputClass =
    "border border-border rounded-md px-3 py-2 text-ink focus:outline-none focus:ring-2 focus:ring-accent focus:border-accent";

  return (
    <form
      action={formAction}
      className="grid grid-cols-2 gap-3 sm:grid-cols-3 md:grid-cols-6 items-end bg-surface border border-border rounded-lg p-4"
    >
      {state?.error && (
        <p role="alert" className="col-span-full bg-danger-bg text-danger text-sm rounded-md px-3 py-2 border border-danger/20">
          {state.error}
        </p>
      )}
      <label className="flex flex-col text-sm gap-1 text-ink-soft">
        Name
        <input name="name" required className={inputClass} />
      </label>
      <label className="flex flex-col text-sm gap-1 text-ink-soft">
        Start
        <input type="time" step={1} name="startTime" required className={inputClass} />
      </label>
      <label className="flex flex-col text-sm gap-1 text-ink-soft">
        End
        <input type="time" step={1} name="endTime" required className={inputClass} />
      </label>
      <label className="flex flex-col text-sm gap-1 text-ink-soft">
        Grace (min)
        <input type="number" name="graceMinutes" defaultValue={0} className={inputClass} />
      </label>
      <label className="flex flex-col text-sm gap-1 text-ink-soft">
        Punch mode
        <select name="punchMode" className={inputClass}>
          <option value="TwoPunch">2-punch (IN/OUT)</option>
          <option value="FourPunch">4-punch (+ breaks)</option>
        </select>
      </label>
      <button
        type="submit"
        disabled={isPending}
        className="bg-accent hover:bg-accent-hover text-accent-ink font-medium rounded-md px-4 py-2 transition-colors disabled:opacity-60"
      >
        Add shift
      </button>
    </form>
  );
}
