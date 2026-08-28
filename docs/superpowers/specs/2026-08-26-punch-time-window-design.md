# Punch Time Window (±30 Minutes) — Design

## Purpose

Today the system accepts a punch at any time — there is no check anywhere
(agent or backend) against the employee's shift schedule. This adds a
business rule: a punch is only accepted within 30 minutes before or after
the shift time it corresponds to (shift start for In, shift end for Out,
and — per the break-punch decision below — the shift's break start/end for
Break Out/Break In). A punch attempted outside that window is rejected with
a message stating the actual accepted window.

## Approach

Enforced entirely on the desktop agent, **before** fingerprint capture
starts. Capture is slow (~16.5s against real ZK4500 hardware, per
`PunchCaptureService.CapturePunchAsync`'s existing comment) and today's
method already fails fast on cheaper checks first (unrecognized employee
code, no cached template) before ever touching the device — the window
check joins that same fail-fast chain, one step further, immediately after
the employee is resolved.

This requires the agent to know the employee's shift start/end/break times,
which it does not cache today (`CachedEmployee` only stores
`EmployeeId`/`EmployeeCode`/`Name`/`CachedAt`). That data has to flow:
backend employee-lookup response → agent's `EmployeeLookupResult` →
`CachedEmployee` local cache — the same path `EmployeeDirectoryService`
already uses for `Name`, just with four more fields riding along.

No timezone conversion is needed anywhere in this feature: the kiosk is a
physical device sitting in the tenant's own location, so
`DateTime.Now.TimeOfDay` on that machine already reads local wall-clock
time. This is simpler than the backend's `AttendanceAnalysisService`, which
has to convert stored UTC timestamps back to local time with a
`DefaultTenantOffset` constant — that machinery isn't needed here because
the comparison never leaves the station.

## Data Flow

**Backend** (`EmployeeLookupController` / `EmployeeLookupResponse`,
`backend/src/AttendanceApi/Dtos/EmployeeDtos.cs`): `EmployeeLookupResponse`
gains four new fields, all `TimeOnly?`:
`ShiftStartTime, ShiftEndTime, ShiftBreakStart, ShiftBreakEnd`. All four are
null when the employee has no `ShiftId`; `ShiftBreakStart`/`ShiftBreakEnd`
are additionally null when the employee's shift doesn't set
`BreakStart`/`BreakEnd` (i.e. most 2-punch shifts). `EmployeeLookupController
.Lookup` loads the employee's `Shift` (when `ShiftId` is set) alongside the
employee to populate these.

**Agent — data model** (`agent/src/AttendanceAgent/Api/ApiDtos.cs`,
`agent/src/AttendanceAgent/Data/CachedEmployee.cs`): `EmployeeLookupResult`
gains the same four nullable `TimeOnly?` fields. `CachedEmployee` gains
matching nullable columns. The agent's SQLite database
(`agent/src/AttendanceAgent/Data/AgentDbContext.cs`) is created via
`Database.EnsureCreated()` with no migration history — adding columns to
the C# model is picked up automatically for any station whose `agent.db`
doesn't exist yet, but **will not** alter an already-existing local database
file (`EnsureCreated()` is a no-op against an existing file). This is an
accepted limitation for now: no station is running against production
hardware today (per project context, only dev-machine test scanners are
attached), so there's no existing `agent.db` this would strand. If/when
real stations are deployed, this will need a real migration story — not
addressed by this design.

**Agent — cache upsert** (`agent/src/AttendanceAgent/Services/
EmployeeDirectoryService.cs`): `UpsertCacheAsync` copies the four new fields
from the lookup result into both the insert and update branches, alongside
the existing `EmployeeCode`/`Name`/`CachedAt` copies. The cache-fallback
path (`ResolveAsync`'s catch block, used when the backend is unreachable)
also returns the four cached fields when constructing its
`EmployeeLookupResult`.

**Agent — enforcement** (`agent/src/AttendanceAgent/Services/
PunchCaptureService.cs`): after `_employees.ResolveAsync(employeeCode, ct)`
succeeds and before the existing template-cache check, map the requested
`punchType` string to the matching shift time:

| `punchType` | Shift time |
|---|---|
| `"In"` | `ShiftStartTime` |
| `"Out"` | `ShiftEndTime` |
| `"BreakOut"` | `ShiftBreakStart` |
| `"BreakIn"` | `ShiftBreakEnd` |

If the mapped time is null (no shift, or no break times configured), skip
the check entirely — that punch type is unrestricted for that employee,
exactly like today. Otherwise, compare `DateTime.Now.TimeOfDay` against
`[mappedTime - 30min, mappedTime + 30min]` (a punch exactly on either
boundary is accepted — inclusive range). Outside that range, return
`PunchResult(false, message)` immediately, without calling into the device
capture or the fingerprint verifier — an out-of-window attempt is rejected
before it ever touches hardware.

**Rejection message**, distinguishing early vs. late:
- Too early: `"Too early to punch {PunchType} — accepted from {windowStart} to {windowEnd}."`
- Too late: `"Too late to punch {PunchType} — accepted from {windowStart} to {windowEnd}."`

`{PunchType}` uses the same display text already on the kiosk buttons ("IN",
"BREAK OUT", "BREAK IN", "OUT" — read from the existing button `Content`
values in `MainWindow.xaml`, lower/mixed-cased to read naturally in a
sentence, e.g. "In", "Break Out"). `{windowStart}`/`{windowEnd}` are
formatted as a 12-hour clock time with AM/PM (e.g. "8:30 AM"), matching how
a person reads a shift schedule.

## Out of Scope / Known Limitations

- **Overnight shifts** (end time earlier than start time, e.g. a night shift 22:00–06:00) work
  correctly for the ±30 minute window itself — each boundary (start/end/break start/break end) is
  checked independently using wrapped minute-of-day arithmetic, so a window straddling midnight
  computes correctly (verified: a 22:00 start gets a correct 21:30–22:30 acceptance window; a 06:00
  end gets a correct 05:30–06:30 window). The only residual limitation is the inherent ±12-hour
  antipode ambiguity of a pure time-of-day comparison with no date component — not a general
  overnight-shift failure.
- **No admin override.** There is no UI path today for an admin to force a
  punch through outside the window from the kiosk — not being added here.
- **Stale cache.** If a shift's times change while a station is offline,
  that station keeps checking against the old cached window until its next
  successful lookup — the same staleness behavior the cache already has for
  `Name`/`EmployeeCode` today.

## Testing

- Backend: `EmployeeLookupController` test asserting the response includes
  the four shift-time fields when the employee has a shift (with and
  without break times set), and all four null when the employee has no
  shift.
- Agent: `PunchCaptureServiceTests` gains cases for each of the 4 punch
  types × {no shift (unrestricted), inside window, exactly at each
  boundary (accepted), just outside each boundary (rejected with the
  correct message and window bounds), no break times on a 2-punch shift
  (unrestricted for Break Out/Break In)} — and confirms the device/verifier
  are never invoked on a rejected attempt (the fail-fast property this
  design depends on).
- `EmployeeDirectoryServiceTests`: cache upsert (both insert and update
  branches) and the offline cache-fallback path both carry the four new
  fields through correctly.

## Scope

Touch: `EmployeeDtos.cs`, `EmployeeLookupController.cs` (backend);
`ApiDtos.cs`, `CachedEmployee.cs`, `EmployeeDirectoryService.cs`,
`PunchCaptureService.cs` (agent), plus their respective test files. No
change to `PunchesController.cs`/the sync batch endpoint (enforcement is
agent-side only, per the approved design) or to `MainViewModel.cs`/
`MainWindow.xaml` (the rejection message flows through the existing
`PunchResult` → `StatusMessage` path, already wired).
