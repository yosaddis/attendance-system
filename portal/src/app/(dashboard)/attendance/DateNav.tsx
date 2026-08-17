"use client";

import { useRouter } from "next/navigation";

export function DateNav({ date }: { date: string }) {
  const router = useRouter();

  return (
    <input
      type="date"
      defaultValue={date}
      onChange={(e) => router.push(`/attendance?date=${e.target.value}`)}
      className="border rounded px-2 py-1"
    />
  );
}
