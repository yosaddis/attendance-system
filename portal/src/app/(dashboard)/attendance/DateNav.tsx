"use client";

import { useRouter } from "next/navigation";

export function DateNav({ date }: { date: string }) {
  const router = useRouter();

  return (
    <input
      type="date"
      defaultValue={date}
      onChange={(e) => router.push(`/attendance?date=${e.target.value}`)}
      className="border border-border rounded-md px-3 py-2 text-ink focus:outline-none focus:ring-2 focus:ring-accent focus:border-accent"
    />
  );
}
