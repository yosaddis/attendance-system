"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";

export function DateRangeNav({ from, to }: { from: string; to: string }) {
  const router = useRouter();
  const [pendingFrom, setPendingFrom] = useState(from);
  const [pendingTo, setPendingTo] = useState(to);

  return (
    <div className="flex flex-wrap items-end gap-3">
      <label className="flex flex-col text-sm text-ink-soft">
        From
        <input
          type="date"
          value={pendingFrom}
          onChange={(e) => setPendingFrom(e.target.value)}
          className="border border-border rounded-md px-3 py-2 text-ink focus:outline-none focus:ring-2 focus:ring-accent focus:border-accent"
        />
      </label>
      <label className="flex flex-col text-sm text-ink-soft">
        To
        <input
          type="date"
          value={pendingTo}
          onChange={(e) => setPendingTo(e.target.value)}
          className="border border-border rounded-md px-3 py-2 text-ink focus:outline-none focus:ring-2 focus:ring-accent focus:border-accent"
        />
      </label>
      <button
        type="button"
        onClick={() => router.push(`/reports?from=${pendingFrom}&to=${pendingTo}`)}
        className="border border-border rounded-md px-4 py-2 text-ink hover:border-accent"
      >
        Apply
      </button>
    </div>
  );
}
