# Employees Full CRUD + Delete Confirmation — Design

## Purpose

The Employees page currently has Create, Read, and Delete, but no Update —
and Delete fires immediately on click with no confirmation. The backend
already implements `PUT /api/employees/{id}` (`EmployeesController.Update`);
it was just never wired into the portal.

## Approach

**Edit:** reuse `EmployeeForm` for both Create and Update rather than
building a second form. It gains an optional `employee?: EmployeeResponse`
prop. When absent, it behaves exactly as today (Create). When present:
Employee Code renders as static read-only text instead of an input (the
code is the identifier used for lookups/punches at the kiosk — not
editable, per the earlier decision), Name/Shift inputs get `defaultValue`
from the employee, the submit button reads "Save changes" instead of "Add
employee", the form's `action` is `updateEmployee.bind(null, employee.id)`
instead of `createEmployee`, and a "Cancel" link appears next to the submit
button pointing back to `/employees`.

`EmployeesPage` reads a `?edit=<id>` search param (the same
search-param-driven pattern already used by Attendance's `date` and
Reports' `from`/`to`). If present, it looks up that employee from the
already-fetched `employees` array (no extra fetch) and passes it to
`EmployeeForm`. Each employee row (desktop table and mobile card) gets a
new "Edit" link to `/employees?edit=<id>`, next to Delete.

**Delete confirmation:** the existing `<form action={deleteEmployee.bind(...)}>`
becomes a small client component, `DeleteEmployeeButton`, so it can call
`window.confirm(...)` before letting the form submit. `onSubmit={(e) => { if
(!confirm(...)) e.preventDefault(); }}` on the form — if the user cancels,
`preventDefault()` stops the server action from ever firing. Both the
desktop table and mobile card list use this component instead of their
current inline forms.

## Backend

No changes — `PUT /api/employees/{id}` already exists and accepts
`{ employeeCode, name, shiftId }` per `CreateEmployeeRequest` (the same
DTO Create uses); the portal will keep sending the existing `employeeCode`
value unchanged in the update payload.

## Testing

- `EmployeeForm.test.tsx` gains cases for edit mode: renders pre-filled
  Name/Shift, renders Code as static text (not an input) when an `employee`
  prop is given, submit button reads "Save changes".
- `actions.test.ts` gains a case for `updateEmployee`: calls `PUT
  /api/employees/{id}` with the right body, revalidates `/employees` on
  success, returns an error state on a 400 (mirroring `createEmployee`'s
  existing error handling).
- New `DeleteEmployeeButton.test.tsx`: clicking Delete with
  `window.confirm` mocked to return `false` does not submit; mocked to
  return `true`, it does.

## Scope

Touch: `EmployeeForm.tsx`, `actions.ts` (add `updateEmployee`),
`employees/page.tsx` (read `?edit=`, add Edit links, swap in
`DeleteEmployeeButton`), new `DeleteEmployeeButton.tsx`. No backend
changes, no changes outside the Employees page.
