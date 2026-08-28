# Attendance Analytics, Dashboard & Reports — Design

## Purpose

The attendance system currently records raw punches and shows a single-day
first-in/last-out view. Tenant admins have no way to see who's late, who's
missing a checkout, whether a punch pair looks like a duplicate scan, how
many hours someone actually worked, or any of this across a date range. This
adds an analytics layer on top of the existing punch data plus three new
surfaces to consume it: a tenant dashboard, an enriched daily attendance
view, and a date-range report with CSV export.

No schema changes are required — `Shift.GraceMinutes`, `Shift.PunchMode`,
and `Shift.StartTime`/`EndTime` already exist and are unused by any
computation today.

## Scope

In scope: the analysis engine, the three portal-facing endpoints it feeds,
and the three portal pages (dashboard, enhanced daily attendance, reports).

Explicitly out of scope for this spec (deferred by the user):
- Per-tenant timezone configuration (see "Timezone" below — a fixed default
  stands in for it now).
- Manual anomaly correction (an admin fixing a missed punch).
- Employee fingerprint-template-enrolled indicator (separate spec).
- Station registration UI (separate spec).
- Password reset, billing/settings, operator console (deferred by the user
  to a later "tenant management" round).

## Timezone

`Tenant` has no timezone field today, and adding one is out of scope here.
Instead, a single constant stands in for "the tenant's timezone" everywhere
day boundaries or time-of-day comparisons matter:

```csharp
// AttendanceApi/Services/AttendanceAnalysisService.cs
public static readonly TimeSpan DefaultTenantOffset = TimeSpan.FromHours(3); // GMT+3, stand-in until per-tenant timezone config exists
```

This is the **only** place the offset is defined. Every day-boundary or
time-of-day calculation in this feature goes through it, so replacing it
with a real `Tenant.TimeZoneOffset` field later is a one-place change:
swap this constant for a per-tenant lookup, nothing else moves.

A "day" is GMT+3 local midnight to local midnight, converted to the UTC
instants `Punch.Timestamp` is actually stored/compared in. A punch's
"time of day" (for late/order comparisons against `Shift.StartTime`, which
is a timezone-less `TimeOnly` representing a local wall-clock time) is
`punch.Timestamp.ToOffset(DefaultTenantOffset).TimeOfDay`.

## Backend: AttendanceAnalysisService

A new pure computation service, `Services/AttendanceAnalysisService.cs`,
takes one employee's punches for one day plus their `Shift` (nullable) and
returns a computed row. It does no DB access itself — fully unit-testable
with in-memory punch lists.

```csharp
public record AttendanceAnalysisResult(
    DateTimeOffset? FirstIn,
    DateTimeOffset? LastOut,
    double? WorkedHours,
    bool HasShift,
    bool IsLate,
    int? LateMinutes,
    bool IsMissingCheckout,
    bool HasDoublePunch);

public static class AttendanceAnalysisService
{
    public static readonly TimeSpan DefaultTenantOffset = TimeSpan.FromHours(3);

    public static AttendanceAnalysisResult Analyze(List<Punch> dayPunches, Shift? shift);
}
```

### Definitions

- **WorkedHours**: punches are sorted by timestamp.
  - `PunchMode.TwoPunch` (or no shift, treated as two-punch): pair up
    consecutive `In`→`Out` punches in order; sum each pair's duration. An
    unpaired trailing `In` (no `Out` yet) contributes nothing to the sum
    (it's reported via `IsMissingCheckout` instead).
  - `PunchMode.FourPunch`: `(BreakOut − In) + (Out − BreakIn)`, using the
    first chronological occurrence of each punch type that day (so a
    double-punched duplicate is ignored for this calculation — it's still
    surfaced via `HasDoublePunch`). If any of the four types doesn't occur
    at all that day, `WorkedHours` is `null` for that day (partial-day math
    isn't attempted — a missing checkout is already surfaced separately).
- **IsLate / LateMinutes**: only computed when `HasShift` is true (no shift
  → both `false`/`null`, per the earlier decision — there's no baseline to
  compare against). Late when the first `In` punch's local time-of-day is
  later than `Shift.StartTime + Shift.GraceMinutes`. `LateMinutes` is the
  (rounded-down) number of minutes past that threshold.
- **IsMissingCheckout**: true when the day's last punch is not `Out` (i.e.
  an `In` or `BreakIn` with nothing after it that day).
- **HasDoublePunch**: true when any two punches of the *same* `PunchType`
  for that employee/day are within 5 minutes of each other.

### Endpoints (`Controllers/AttendanceController.cs`)

All three require `AuthorizationPolicies.TenantAdmin`, matching the
existing `Daily` action.

1. **`GET /api/attendance/daily?date=`** (extended, not replaced) — now
   starts from `Employees` (tenant, with or without a shift) left-joined to
   that day's punches, instead of starting from punches — today, an
   employee with zero punches never appears at all, which is how "absent"
   silently disappears instead of showing up. Response DTO gains the new
   analysis fields:

   ```csharp
   public record AttendanceRowResponse(
       Guid EmployeeId,
       string EmployeeName,
       DateOnly Date,
       DateTimeOffset? FirstIn,
       DateTimeOffset? LastOut,
       double? WorkedHours,
       bool HasShift,
       bool IsLate,
       int? LateMinutes,
       bool IsMissingCheckout,
       bool HasDoublePunch);
   ```

2. **`GET /api/attendance/summary?date=`** (new) — tenant-wide counts for
   the dashboard, built from the same per-employee rows:

   ```csharp
   public record AttendanceSummaryResponse(
       int TotalEmployeesWithShift,
       int PresentCount,   // has a shift AND at least one punch that day
       int AbsentCount,    // has a shift AND zero punches that day
       int LateCount);     // has a shift AND IsLate
   ```

   Employees without a shift are excluded from all three counts (they have
   no baseline to be "present/absent/late" against) but still counted
   elsewhere (e.g. total employee count on the Employees page is unrelated
   to this).

   Known simplification: "absent" is evaluated at whatever moment the
   dashboard is loaded — an employee whose shift starts at 2pm will show as
   absent all morning until they punch in. This matches how a live
   headcount dashboard is generally expected to behave and isn't treated as
   a bug.

3. **`GET /api/attendance/report?from=&to=`** (new) — one `AttendanceRowResponse`
   per employee per day across `[from, to]` inclusive, skipping any
   employee-day with neither a shift nor any punches (avoids a sparse
   matrix of all-blank rows). Returns `400 Bad Request` if `to < from` or
   the range exceeds 90 days.

4. **`GET /api/attendance/report/export?from=&to=`** (new) — identical data
   to (3), serialized as CSV (`text/csv`, `Content-Disposition:
   attachment; filename="attendance-{from}-to-{to}.csv"`). Same 90-day cap
   and validation. Columns: Employee, Date, First In, Last Out, Worked
   Hours, Late (minutes, blank if not late/no shift), Missing Checkout
   (yes/blank), Double Punch (yes/blank).

## Portal

### `/dashboard` (new — replaces `/attendance` as the post-login landing page)

Server component, fetches `/api/attendance/summary?date=<today, GMT+3>`.
Four KPI cards (Present, Absent, Late, Total with shift), same card/border
treatment as the rest of the redesigned portal. `src/app/page.tsx`'s
redirect changes from `/attendance` to `/dashboard`. Nav gets a new
"Dashboard" link, first in the list.

### `/attendance` (existing, extended)

Same page, extended columns: Shift name (or "—" if none), Worked Hours
(blank if null), and a small badge cluster for Late / Missing Checkout /
Double Punch (only rendered when true — no "OK" badge for the normal case,
to keep the table calm). Absent employees now appear with blank First
In/Last Out and no badges (there's nothing to flag — "absent" is implied by
the blank cells plus the dashboard's count, not a per-row badge, since the
row itself IS the signal).

### `/reports` (new)

A from/to date input pair (defaulting to the last 7 days), the same
enriched table as `/attendance` but with a Date column added (since it
spans multiple days), and an "Export CSV" link.

The portal talks to the backend with a server-side bearer token
(`getToken()` from a session cookie) that never reaches the browser, so the
export link can't just point at the backend directly. It points at a new
portal route handler:

```
portal/src/app/(dashboard)/reports/export/route.ts
```

which reads the session token server-side, fetches the backend's CSV
export endpoint, and streams the response back with the same
`Content-Disposition` header — so from the browser's perspective it's a
plain link that downloads a file, no client-side token handling anywhere.

`backendFetch` (which always calls `.json()`) isn't reused here — this
route handler does its own `fetch` against `BACKEND_API_URL` and pipes the
raw response through.

## Error Handling

- Invalid/out-of-range `date`/`from`/`to` query params → `400 Bad Request`
  with a message, same pattern as existing controllers (e.g.
  `EmployeesController`'s `BadRequest($"...")`).
- `to < from` or range > 90 days → `400 Bad Request`.
- A day with an incomplete four-punch sequence → `WorkedHours: null` for
  that row, not an error (see "Definitions" above).
- Portal pages: invalid date params fall back to sensible defaults (today
  for the dashboard/daily view, last 7 days for reports) the same way
  `attendance/page.tsx` already falls back to today for a bad `date` query
  param.

## Testing

- `AttendanceAnalysisService`: unit tests, no DB — on-time vs late vs no
  shift, missing checkout, double-punch within 5 minutes vs. further apart,
  two-punch worked-hours math, four-punch worked-hours math, incomplete
  four-punch sequence (null hours).
- `AttendanceController`: integration tests for `daily` (now including
  absent employees), `summary` (counts), `report` (range + skip logic +
  90-day cap), `report/export` (CSV shape + headers).
- Portal: component tests for the dashboard cards, the extended attendance
  table (badges render only when true), and the reports page (date inputs,
  table, export link href), following the existing pattern in
  `attendance/page.test.tsx`.
