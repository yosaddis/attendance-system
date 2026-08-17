"use client";

import Link from "next/link";
import { useEffect } from "react";

export default function DashboardError({
  error,
  retry,
}: {
  error: Error & { digest?: string };
  retry: () => void;
}) {
  useEffect(() => {
    console.error(error);
  }, [error]);

  return (
    <div className="p-4 space-y-4">
      <h1 className="text-xl font-semibold">Something went wrong</h1>
      <p className="text-gray-600">
        We couldn&apos;t complete that request. You can try again, or head back to a working
        page using the navigation above.
      </p>
      <div className="flex gap-4">
        <button
          type="button"
          onClick={() => retry()}
          className="bg-blue-600 text-white rounded px-3 py-1"
        >
          Try again
        </button>
        <Link href="/attendance" className="text-blue-600 underline">
          Back to Attendance
        </Link>
      </div>
    </div>
  );
}
