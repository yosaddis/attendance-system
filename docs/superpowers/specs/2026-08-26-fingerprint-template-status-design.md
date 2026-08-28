# Fingerprint Template Status — Design

## Purpose

The Employees page has no way to tell which employees still need fingerprint
enrollment. An admin has to go enroll someone at the kiosk to find out
whether they were already enrolled. This adds a simple enrolled/not-enrolled
indicator per employee, visible on the Employees page.

## Approach

`EmployeeResponse` (backend) gains a `HasFingerprint: bool`. The Employees
page shows a red "Not Enrolled" badge next to employees missing a template;
enrolled employees show nothing extra — consistent with the existing
anomaly-badge convention on the Attendance page, where badges only ever flag
something that needs attention, not the good/expected state.

## Data Flow

**Backend** (`backend/src/AttendanceApi/Dtos/EmployeeDtos.cs`,
`Controllers/EmployeesController.cs`): `EmployeeResponse` gains
`HasFingerprint: bool` as its last field:
`record EmployeeResponse(Guid Id, string EmployeeCode, string Name, Guid? ShiftId, bool HasFingerprint)`.

`EmployeesController.List()` currently maps each `Employee` to a response
with no knowledge of `FingerprintTemplate` at all. To avoid an N+1 query (one
`FingerprintTemplates` lookup per employee), `List()` first loads the set of
employee IDs that have a template in one query
(`_db.FingerprintTemplates.Where(t => employeeIds.Contains(t.EmployeeId)).Select(t => t.EmployeeId).ToHashSetAsync()`,
scoped to the tenant's employee IDs already loaded), then passes that set
into `ToResponse` so each row's `HasFingerprint` is a hash-set lookup, not a
query. `Get(id)` (single-employee fetch) does its own single
`FingerprintTemplates.AnyAsync(t => t.EmployeeId == id)` check — no N+1
concern for a single row.

**Portal** (`portal/src/app/(dashboard)/employees/page.tsx`,
`src/lib/types.ts`): `EmployeeResponse` type gains `hasFingerprint: boolean`.
Both the desktop table and the mobile card list (the same dual-view pattern
already established for the Employees page by the CRUD feature) render the
existing shared `Badge` component
(`portal/src/app/(dashboard)/Badge.tsx`) reading "Not Enrolled" next to an
employee's name/code when `hasFingerprint` is `false`; nothing is rendered
when `true`.

## Testing

- Backend: `EmployeesController` test asserting `List()` returns
  `HasFingerprint: true` for an employee with an enrolled template and
  `false` for one without, in the same tenant-scoped list (covering the
  hash-set-based mapping, not just a single row).
- Portal: `employees/page.test.tsx` (added by the Employee CRUD feature)
  gains a case asserting the "Not Enrolled" badge appears for an unenrolled
  employee and is absent for an enrolled one, checked in both the desktop
  table and the `mobile-cards` block.

## Scope

Touch: `EmployeeDtos.cs`, `EmployeesController.cs` (backend);
`types.ts`, `employees/page.tsx`, `employees/page.test.tsx` (portal). No
change to `Badge.tsx` itself (existing single-variant component is reused
as-is), no change to `TemplatesController.cs`, no change to the desktop
agent.
