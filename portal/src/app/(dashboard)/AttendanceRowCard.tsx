import { Badge } from "./Badge";
import type { AttendanceRowResponse } from "@/lib/types";
import { formatTenantTime } from "@/lib/tenantTime";

export function AttendanceRowCard({
  row,
  shiftName,
  showDate,
}: {
  row: AttendanceRowResponse;
  shiftName: string;
  showDate: boolean;
}) {
  const hasAnyFlag = row.isLate || row.isMissingCheckout || row.hasDoublePunch;

  return (
    <div className="p-4">
      <div className="flex items-baseline justify-between gap-2">
        <div className="font-medium text-ink">{row.employeeName}</div>
        {showDate && <div className="text-xs text-ink-soft shrink-0">{row.date}</div>}
      </div>
      <div className="text-sm text-ink mt-1">
        {row.firstIn ? formatTenantTime(row.firstIn) : "—"} – {row.lastOut ? formatTenantTime(row.lastOut) : "—"}
      </div>
      {hasAnyFlag && (
        <div className="mt-1">
          {row.isLate && <Badge>Late{row.lateMinutes !== null ? ` (${row.lateMinutes}m)` : ""}</Badge>}
          {row.isMissingCheckout && <Badge>Missing Checkout</Badge>}
          {row.hasDoublePunch && <Badge>Double Punch</Badge>}
        </div>
      )}
      <div className="text-xs text-ink-soft mt-1">
        {shiftName}
        {row.workedHours !== null ? ` · ${row.workedHours.toFixed(2)}h` : ""}
      </div>
    </div>
  );
}
