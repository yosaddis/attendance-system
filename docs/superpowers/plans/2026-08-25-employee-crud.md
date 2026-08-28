# Employees Full CRUD + Delete Confirmation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add employee editing (the missing Update in CRUD) and a confirmation prompt before deleting an employee.

**Architecture:** `EmployeeForm` gains an optional `employee` prop, switching it between Create and Update mode (same component, no duplication). `EmployeesPage` reads a `?edit=<id>` search param to decide which mode to render. Delete moves into a small client component so it can gate the submit on `window.confirm`.

**Tech Stack:** Next.js 16 / React 19 server components + one client component, Vitest + Testing Library.

## Global Constraints

- Employee Code is never editable after creation — Edit only changes Name and Shift.
- Backend needs no changes — `PUT /api/employees/{id}` already exists and accepts the same `{ employeeCode, name, shiftId }` shape as Create.
- Delete confirmation uses the browser's native `window.confirm(...)`, not a custom dialog.

---

### Task 1: Employee editing (Update)

**Files:**
- Modify: `portal/src/app/(dashboard)/employees/EmployeeForm.tsx`
- Modify: `portal/src/app/(dashboard)/employees/EmployeeForm.test.tsx`
- Modify: `portal/src/app/(dashboard)/employees/actions.ts`
- Modify: `portal/src/app/(dashboard)/employees/actions.test.ts`
- Modify: `portal/src/app/(dashboard)/employees/page.tsx`

**Interfaces:**
- Produces: `EmployeeForm({ shifts, employee? }: { shifts: ShiftResponse[]; employee?: EmployeeResponse })`, `updateEmployee(id: string, prevState: CreateEmployeeState, formData: FormData): Promise<CreateEmployeeState>` exported from `actions.ts`.

- [ ] **Step 1: Write the failing tests**

Append to `portal/src/app/(dashboard)/employees/EmployeeForm.test.tsx` (inside the `describe` block):

```tsx
  it("pre-fills name and shift, renders the code read-only, and labels the submit button 'Save changes' in edit mode", () => {
    render(
      <EmployeeForm
        shifts={[
          {
            id: "s1",
            name: "Day Shift",
            startTime: "09:00:00",
            endTime: "17:00:00",
            graceMinutes: 10,
            punchMode: "TwoPunch",
            breakStart: null,
            breakEnd: null,
            allowedBreakMinutes: null,
          },
        ]}
        employee={{ id: "e1", employeeCode: "E001", name: "Jane Doe", shiftId: "s1" }}
      />,
    );

    const codeInput = screen.getByLabelText(/employee code/i) as HTMLInputElement;
    expect(codeInput).toHaveValue("E001");
    expect(codeInput).toHaveAttribute("readonly");
    expect(screen.getByLabelText(/^name$/i)).toHaveValue("Jane Doe");
    expect(screen.getByLabelText(/shift/i)).toHaveValue("s1");
    expect(screen.getByRole("button", { name: /save changes/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /cancel/i })).toHaveAttribute("href", "/employees");
  });

  it("has no read-only code field and no cancel link when creating (no employee prop)", () => {
    render(<EmployeeForm shifts={[]} />);

    expect(screen.getByLabelText(/employee code/i)).not.toHaveAttribute("readonly");
    expect(screen.getByRole("button", { name: /add employee/i })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /cancel/i })).not.toBeInTheDocument();
  });
```

Append to `portal/src/app/(dashboard)/employees/actions.test.ts` (inside the `describe` block; also add `redirectMock` mocking — see Step 1b):

```tsx
  it("PUTs the updated fields to /api/employees/{id} and redirects on success", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ id: "e1" }), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    const formData = new FormData();
    formData.set("employeeCode", "E001");
    formData.set("name", "Jane Renamed");
    formData.set("shiftId", "s2");

    await expect(updateEmployee("e1", null, formData)).rejects.toThrow("REDIRECT:/employees");

    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toContain("/api/employees/e1");
    expect(init.method).toBe("PUT");
    const body = JSON.parse(init.body as string);
    expect(body).toEqual({ employeeCode: "E001", name: "Jane Renamed", shiftId: "s2" });
    expect(revalidatePath).toHaveBeenCalledWith("/employees");
  });

  it("returns an error state instead of throwing when the backend rejects an update", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response("shift does not exist", { status: 400 }));
    vi.stubGlobal("fetch", fetchMock);

    const formData = new FormData();
    formData.set("employeeCode", "E001");
    formData.set("name", "Jane Doe");

    const state = await updateEmployee("e1", null, formData);

    expect(state?.error).toBe("Could not save employee: shift does not exist");
    expect(revalidatePath).not.toHaveBeenCalled();
  });
```

Step 1b — add the `redirect` mock and import to the top of `actions.test.ts` (the file currently mocks `next/headers` and `next/cache` but not `next/navigation`):

```ts
const redirectMock = vi.fn((url: string) => {
  throw new Error(`REDIRECT:${url}`);
});
vi.mock("next/navigation", () => ({ redirect: redirectMock }));
```

Place this alongside the existing `vi.mock("next/headers", ...)`/`vi.mock("next/cache", ...)` calls, and change the import line `import { createEmployee, deleteEmployee } from "./actions";` to `import { createEmployee, deleteEmployee, updateEmployee } from "./actions";`.

- [ ] **Step 2: Run the tests to verify they fail**

Run (from `portal/`): `npm test -- employees`
Expected: FAIL — `updateEmployee` doesn't exist yet; `EmployeeForm` doesn't accept an `employee` prop yet.

- [ ] **Step 3: Update EmployeeForm.tsx**

Replace the contents of `portal/src/app/(dashboard)/employees/EmployeeForm.tsx`:

```tsx
"use client";

import { useActionState } from "react";
import type { EmployeeResponse, ShiftResponse } from "@/lib/types";
import { createEmployee, updateEmployee, type CreateEmployeeState } from "./actions";

export function EmployeeForm({ shifts, employee }: { shifts: ShiftResponse[]; employee?: EmployeeResponse }) {
  const action = employee ? updateEmployee.bind(null, employee.id) : createEmployee;
  const [state, formAction, isPending] = useActionState<CreateEmployeeState, FormData>(action, null);

  return (
    <form
      action={formAction}
      className="grid grid-cols-2 gap-3 sm:grid-cols-4 items-end bg-surface border border-border rounded-lg p-4"
    >
      {state?.error && (
        <p role="alert" className="col-span-full bg-danger-bg text-danger text-sm rounded-md px-3 py-2 border border-danger/20">
          {state.error}
        </p>
      )}
      <label htmlFor="employeeCode" className="flex flex-col text-sm gap-1 text-ink-soft">
        Employee code
        <input
          id="employeeCode"
          name="employeeCode"
          required
          readOnly={!!employee}
          defaultValue={employee?.employeeCode}
          className={
            "border border-border rounded-md px-3 py-2 text-ink focus:outline-none " +
            (employee
              ? "bg-surface-muted text-ink-soft cursor-not-allowed"
              : "focus:ring-2 focus:ring-accent focus:border-accent")
          }
        />
      </label>
      <label htmlFor="name" className="flex flex-col text-sm gap-1 text-ink-soft">
        Name
        <input
          id="name"
          name="name"
          required
          defaultValue={employee?.name}
          className="border border-border rounded-md px-3 py-2 text-ink focus:outline-none focus:ring-2 focus:ring-accent focus:border-accent"
        />
      </label>
      <label htmlFor="shiftId" className="flex flex-col text-sm gap-1 text-ink-soft">
        Shift
        <select
          id="shiftId"
          name="shiftId"
          defaultValue={employee?.shiftId ?? ""}
          className="border border-border rounded-md px-3 py-2 text-ink focus:outline-none focus:ring-2 focus:ring-accent focus:border-accent"
        >
          <option value="">— None —</option>
          {shifts.map((shift) => (
            <option key={shift.id} value={shift.id}>
              {shift.name}
            </option>
          ))}
        </select>
      </label>
      <div className="flex gap-3">
        <button
          type="submit"
          disabled={isPending}
          className="bg-accent hover:bg-accent-hover text-accent-ink font-medium rounded-md px-4 py-2 transition-colors disabled:opacity-60"
        >
          {employee ? "Save changes" : "Add employee"}
        </button>
        {employee && (
          <a
            href="/employees"
            className="border border-border rounded-md px-4 py-2 text-ink hover:border-accent flex items-center"
          >
            Cancel
          </a>
        )}
      </div>
    </form>
  );
}
```

- [ ] **Step 4: Add updateEmployee to actions.ts**

Add to `portal/src/app/(dashboard)/employees/actions.ts`, after the existing `createEmployee` function and before `deleteEmployee` (and add `import { redirect } from "next/navigation";` to the top of the file alongside the existing imports):

```ts

export async function updateEmployee(
  id: string,
  _prevState: CreateEmployeeState,
  formData: FormData,
): Promise<CreateEmployeeState> {
  const shiftId = formData.get("shiftId");

  try {
    await backendFetch(`/api/employees/${id}`, {
      method: "PUT",
      token: await getToken(),
      body: JSON.stringify({
        employeeCode: String(formData.get("employeeCode")),
        name: String(formData.get("name")),
        shiftId: shiftId ? String(shiftId) : null,
      }),
    });
  } catch (err) {
    if (err instanceof BackendError && err.status === 400) {
      return { error: `Could not save employee: ${err.message || "please check the details and try again."}` };
    }
    throw err;
  }

  revalidatePath("/employees");
  redirect("/employees");
}
```

- [ ] **Step 5: Wire editingEmployee lookup into the page**

In `portal/src/app/(dashboard)/employees/page.tsx`, change the function signature and add the lookup (this file is also touched by Task 2 below for the Edit/Delete links — this step only adds the `searchParams` plumbing and the `<EmployeeForm>` prop):

```tsx
export default async function EmployeesPage({
  searchParams,
}: {
  searchParams: Promise<{ edit?: string }>;
}) {
  const { edit } = await searchParams;
  const token = await getToken();
  const [employees, shifts]: [EmployeeResponse[], ShiftResponse[]] = await Promise.all([
    backendFetch("/api/employees", { token }),
    backendFetch("/api/shifts", { token }),
  ]);
  const editingEmployee = edit ? employees.find((e) => e.id === edit) : undefined;
```

and change `<EmployeeForm shifts={shifts} />` to `<EmployeeForm shifts={shifts} employee={editingEmployee} />`.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `npm test -- employees`
Expected: PASS (all tests in `EmployeeForm.test.tsx` and `actions.test.ts`)

- [ ] **Step 7: Commit**

```bash
git add "portal/src/app/(dashboard)/employees/EmployeeForm.tsx" "portal/src/app/(dashboard)/employees/EmployeeForm.test.tsx" "portal/src/app/(dashboard)/employees/actions.ts" "portal/src/app/(dashboard)/employees/actions.test.ts" "portal/src/app/(dashboard)/employees/page.tsx"
git commit -m "feat: add employee editing"
```

---

### Task 2: Delete confirmation + Edit links

**Files:**
- Create: `portal/src/app/(dashboard)/employees/DeleteEmployeeButton.tsx`
- Create: `portal/src/app/(dashboard)/employees/DeleteEmployeeButton.test.tsx`
- Modify: `portal/src/app/(dashboard)/employees/page.tsx`

**Interfaces:**
- Consumes: `deleteEmployee` from `./actions` (unchanged, existing).
- Produces: `DeleteEmployeeButton({ id, name }: { id: string; name: string })`.

- [ ] **Step 1: Write the failing test**

```tsx
// portal/src/app/(dashboard)/employees/DeleteEmployeeButton.test.tsx
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";

vi.mock("./actions", () => ({ deleteEmployee: vi.fn(() => () => {}) }));

import { DeleteEmployeeButton } from "./DeleteEmployeeButton";

describe("DeleteEmployeeButton", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it("asks for confirmation naming the employee before deleting", () => {
    const confirmSpy = vi.spyOn(window, "confirm").mockReturnValue(false);
    render(<DeleteEmployeeButton id="e1" name="Jane Doe" />);

    fireEvent.click(screen.getByRole("button", { name: /delete/i }));

    expect(confirmSpy).toHaveBeenCalledWith("Delete Jane Doe? This cannot be undone.");
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `npm test -- DeleteEmployeeButton.test.tsx`
Expected: FAIL — `./DeleteEmployeeButton` doesn't exist yet.

- [ ] **Step 3: Create DeleteEmployeeButton.tsx**

```tsx
// portal/src/app/(dashboard)/employees/DeleteEmployeeButton.tsx
"use client";

import { deleteEmployee } from "./actions";

export function DeleteEmployeeButton({ id, name }: { id: string; name: string }) {
  return (
    <form
      action={deleteEmployee.bind(null, id)}
      onSubmit={(e) => {
        if (!confirm(`Delete ${name}? This cannot be undone.`)) {
          e.preventDefault();
        }
      }}
    >
      <button type="submit" className="text-danger hover:underline text-sm">
        Delete
      </button>
    </form>
  );
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `npm test -- DeleteEmployeeButton.test.tsx`
Expected: PASS

- [ ] **Step 5: Wire Edit links and DeleteEmployeeButton into the page**

Replace the contents of `portal/src/app/(dashboard)/employees/page.tsx`:

```tsx
import { backendFetch } from "@/lib/backendFetch";
import { getToken } from "@/lib/session";
import type { EmployeeResponse, ShiftResponse } from "@/lib/types";
import { EmployeeForm } from "./EmployeeForm";
import { DeleteEmployeeButton } from "./DeleteEmployeeButton";

export default async function EmployeesPage({
  searchParams,
}: {
  searchParams: Promise<{ edit?: string }>;
}) {
  const { edit } = await searchParams;
  const token = await getToken();
  const [employees, shifts]: [EmployeeResponse[], ShiftResponse[]] = await Promise.all([
    backendFetch("/api/employees", { token }),
    backendFetch("/api/shifts", { token }),
  ]);
  const editingEmployee = edit ? employees.find((e) => e.id === edit) : undefined;

  return (
    <div className="p-6 space-y-6">
      <h1 className="font-display text-2xl text-ink">Employees</h1>
      <EmployeeForm shifts={shifts} employee={editingEmployee} />
      <div className="hidden md:block overflow-x-auto bg-surface border border-border rounded-lg">
        <table className="w-full text-sm border-collapse">
          <thead>
            <tr className="text-left bg-surface-muted text-ink">
              <th className="py-3 px-4 font-medium">Code</th>
              <th className="py-3 px-4 font-medium">Name</th>
              <th className="py-3 px-4 font-medium">Shift</th>
              <th className="py-3 px-4"></th>
            </tr>
          </thead>
          <tbody>
            {employees.map((employee) => (
              <tr key={employee.id} className="border-b border-border last:border-b-0 hover:bg-surface-muted">
                <td className="py-3 px-4">{employee.employeeCode}</td>
                <td className="py-3 px-4">{employee.name}</td>
                <td className="py-3 px-4">{shifts.find((s) => s.id === employee.shiftId)?.name ?? "—"}</td>
                <td className="py-3 px-4">
                  <div className="flex gap-3 items-center">
                    <a href={`/employees?edit=${employee.id}`} className="text-ink hover:text-accent text-sm">
                      Edit
                    </a>
                    <DeleteEmployeeButton id={employee.id} name={employee.name} />
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <div
        data-testid="mobile-cards"
        className="md:hidden divide-y divide-border bg-surface border border-border rounded-lg"
      >
        {employees.map((employee) => (
          <div key={employee.id} className="p-4 flex items-center justify-between gap-3">
            <div>
              <div className="font-medium text-ink">{employee.name}</div>
              <div className="text-xs text-ink-soft mt-1">
                {employee.employeeCode} · {shifts.find((s) => s.id === employee.shiftId)?.name ?? "—"}
              </div>
            </div>
            <div className="flex gap-3 items-center shrink-0">
              <a href={`/employees?edit=${employee.id}`} className="text-ink hover:text-accent text-sm">
                Edit
              </a>
              <DeleteEmployeeButton id={employee.id} name={employee.name} />
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}
```

- [ ] **Step 6: Run the full portal test suite**

Run: `npm test`
Expected: PASS (all tests, no regressions)

- [ ] **Step 7: Manually verify in the browser**

Run: `npm run build && npm run start` (backend must be running)
- On Employees, click "Edit" for an employee: confirm the form pre-fills, the code field is visibly read-only, "Save changes" updates the row and returns to the plain `/employees` URL, "Cancel" returns without saving.
- Click "Delete": confirm a browser confirm dialog appears naming the employee; declining leaves the employee in place; accepting removes it.

- [ ] **Step 8: Commit**

```bash
git add "portal/src/app/(dashboard)/employees/DeleteEmployeeButton.tsx" "portal/src/app/(dashboard)/employees/DeleteEmployeeButton.test.tsx" "portal/src/app/(dashboard)/employees/page.tsx"
git commit -m "feat: confirm before deleting an employee, add Edit links"
```
