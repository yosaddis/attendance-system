# Fingerprint Template Status Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show a "Not Enrolled" badge on the Employees page for any employee without an enrolled fingerprint template.

**Architecture:** `EmployeeResponse` gains a `HasFingerprint` bool, computed via a single set-membership check (not a per-row query) in `EmployeesController.List()`. The Employees page renders the existing shared `Badge` component when an employee is missing a template.

**Tech Stack:** ASP.NET Core 8 + EF Core (backend), Next.js 16 / React 19 (portal), xUnit + Vitest.

## Global Constraints

- Enrolled employees show nothing extra — badges only ever flag something needing attention, matching the existing anomaly-badge convention on the Attendance page.
- `List()` must not N+1-query `FingerprintTemplates` per employee — use one query for the whole tenant-scoped set.
- No change to `Badge.tsx` itself, `TemplatesController.cs`, or the desktop agent.

---

### Task 1: Backend — HasFingerprint on EmployeeResponse

**Files:**
- Modify: `backend/src/AttendanceApi/Dtos/EmployeeDtos.cs`
- Modify: `backend/src/AttendanceApi/Controllers/EmployeesController.cs`
- Modify: `backend/tests/AttendanceApi.Tests/EmployeesControllerTests.cs`

**Interfaces:**
- Produces: `EmployeeResponse(Guid Id, string EmployeeCode, string Name, Guid? ShiftId, bool HasFingerprint)`.

- [ ] **Step 1: Write the failing tests**

Add these 2 tests to `backend/tests/AttendanceApi.Tests/EmployeesControllerTests.cs`, right after `CreateEmployee_ThenDuplicateCode_Returns400`:

```csharp
    [Fact]
    public async Task CreateEmployee_ReturnsHasFingerprintFalse()
    {
        var tenantId = CreateTenant();
        var client = await TenantAdminClientAsync(tenantId);

        var created = await client.PostAsJsonAsync("/api/employees", new CreateEmployeeRequest("E001", "Jane Doe", null));
        var employee = await created.Content.ReadFromJsonAsync<EmployeeResponse>();

        Assert.False(employee!.HasFingerprint);
    }

    [Fact]
    public async Task ListEmployees_ReflectsFingerprintEnrollmentStatus()
    {
        var tenantId = CreateTenant();
        var client = await TenantAdminClientAsync(tenantId);

        var enrolledCreated = await client.PostAsJsonAsync("/api/employees", new CreateEmployeeRequest("E001", "Jane Doe", null));
        var enrolled = await enrolledCreated.Content.ReadFromJsonAsync<EmployeeResponse>();
        var unenrolledCreated = await client.PostAsJsonAsync("/api/employees", new CreateEmployeeRequest("E002", "John Smith", null));
        var unenrolled = await unenrolledCreated.Content.ReadFromJsonAsync<EmployeeResponse>();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var cipher = scope.ServiceProvider.GetRequiredService<ITemplateCipher>();
            db.FingerprintTemplates.Add(new FingerprintTemplate
            {
                EmployeeId = enrolled!.Id,
                Vendor = DeviceVendor.Zk4500,
                TemplateDataEncrypted = cipher.Encrypt(new byte[] { 1, 2, 3 }),
            });
            db.SaveChanges();
        }

        var listResponse = await client.GetAsync("/api/employees");
        var list = await listResponse.Content.ReadFromJsonAsync<List<EmployeeResponse>>();

        Assert.True(list!.Single(e => e.Id == enrolled.Id).HasFingerprint);
        Assert.False(list.Single(e => e.Id == unenrolled!.Id).HasFingerprint);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run (from `backend/`): `dotnet test --filter EmployeesControllerTests`
Expected: FAIL — `EmployeeResponse` has no `HasFingerprint` member yet (compile error).

- [ ] **Step 3: Extend EmployeeResponse**

In `backend/src/AttendanceApi/Dtos/EmployeeDtos.cs`, replace:

```csharp
public record EmployeeResponse(Guid Id, string EmployeeCode, string Name, Guid? ShiftId);
```

with:

```csharp
public record EmployeeResponse(Guid Id, string EmployeeCode, string Name, Guid? ShiftId, bool HasFingerprint);
```

- [ ] **Step 4: Update EmployeesController**

In `backend/src/AttendanceApi/Controllers/EmployeesController.cs`, replace the `List` method:

```csharp
    [HttpGet]
    public async Task<ActionResult<List<EmployeeResponse>>> List()
    {
        var tenantId = User.TenantId()!.Value;
        var employees = await _db.Employees.Where(e => e.TenantId == tenantId).ToListAsync();
        return employees.Select(ToResponse).ToList();
    }
```

with:

```csharp
    [HttpGet]
    public async Task<ActionResult<List<EmployeeResponse>>> List()
    {
        var tenantId = User.TenantId()!.Value;
        var employees = await _db.Employees.Where(e => e.TenantId == tenantId).ToListAsync();

        var employeeIds = employees.Select(e => e.Id).ToList();
        var withTemplate = (await _db.FingerprintTemplates
            .Where(t => employeeIds.Contains(t.EmployeeId))
            .Select(t => t.EmployeeId)
            .ToListAsync()).ToHashSet();

        return employees.Select(e => ToResponse(e, withTemplate.Contains(e.Id))).ToList();
    }
```

Replace the `Get` method:

```csharp
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<EmployeeResponse>> Get(Guid id)
    {
        var tenantId = User.TenantId()!.Value;
        var employee = await _db.Employees.SingleOrDefaultAsync(e => e.Id == id && e.TenantId == tenantId);
        return employee is null ? NotFound() : ToResponse(employee);
    }
```

with:

```csharp
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<EmployeeResponse>> Get(Guid id)
    {
        var tenantId = User.TenantId()!.Value;
        var employee = await _db.Employees.SingleOrDefaultAsync(e => e.Id == id && e.TenantId == tenantId);
        if (employee is null) return NotFound();

        var hasFingerprint = await _db.FingerprintTemplates.AnyAsync(t => t.EmployeeId == id);
        return ToResponse(employee, hasFingerprint);
    }
```

In the `Create` method, replace the final line:

```csharp
        return CreatedAtAction(nameof(Get), new { id = employee.Id }, ToResponse(employee));
```

with:

```csharp
        // A brand-new employee's Id was just generated — no FingerprintTemplate row can exist for
        // it yet, so this is always false without needing a query.
        return CreatedAtAction(nameof(Get), new { id = employee.Id }, ToResponse(employee, hasFingerprint: false));
```

In the `Update` method, replace the final two lines:

```csharp
        employee.Name = request.Name;
        employee.ShiftId = request.ShiftId;
        await _db.SaveChangesAsync();
        return ToResponse(employee);
```

with:

```csharp
        employee.Name = request.Name;
        employee.ShiftId = request.ShiftId;
        await _db.SaveChangesAsync();

        var hasFingerprint = await _db.FingerprintTemplates.AnyAsync(t => t.EmployeeId == employee.Id);
        return ToResponse(employee, hasFingerprint);
```

Replace the `ToResponse` helper:

```csharp
    private static EmployeeResponse ToResponse(Employee e) => new(e.Id, e.EmployeeCode, e.Name, e.ShiftId);
```

with:

```csharp
    private static EmployeeResponse ToResponse(Employee e, bool hasFingerprint) => new(e.Id, e.EmployeeCode, e.Name, e.ShiftId, hasFingerprint);
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test --filter EmployeesControllerTests`
Expected: PASS (all tests — existing + 2 new)

- [ ] **Step 6: Run the full backend test suite**

Run: `dotnet test`
Expected: PASS (no regressions)

- [ ] **Step 7: Commit**

```bash
git add backend/src/AttendanceApi/Dtos/EmployeeDtos.cs backend/src/AttendanceApi/Controllers/EmployeesController.cs backend/tests/AttendanceApi.Tests/EmployeesControllerTests.cs
git commit -m "feat: report fingerprint enrollment status on employee responses"
```

---

### Task 2: Portal — show the Not Enrolled badge

**Files:**
- Modify: `portal/src/lib/types.ts`
- Modify: `portal/src/app/(dashboard)/employees/page.tsx`
- Modify: `portal/src/app/(dashboard)/employees/page.test.tsx`

**Interfaces:**
- Consumes: `EmployeeResponse.hasFingerprint: boolean` (Task 1's backend field, camelCased in the JSON response per this app's existing convention — every other field on this type is already camelCase).
- Consumes: `Badge` from `../Badge` (existing, unchanged).

- [ ] **Step 1: Write the failing tests**

Update the `employees` fixture array near the top of `portal/src/app/(dashboard)/employees/page.test.tsx` — replace:

```tsx
const employees = [
  { id: "e1", employeeCode: "E001", name: "Jane Doe", shiftId: "s1" },
  { id: "e2", employeeCode: "E002", name: "John Smith", shiftId: null },
];
```

with:

```tsx
const employees = [
  { id: "e1", employeeCode: "E001", name: "Jane Doe", shiftId: "s1", hasFingerprint: true },
  { id: "e2", employeeCode: "E002", name: "John Smith", shiftId: null, hasFingerprint: false },
];
```

Add this test at the end of the `describe` block, before its closing brace:

```tsx
  it("shows a Not Enrolled badge only for employees without a fingerprint template", async () => {
    render(await EmployeesPage({ searchParams: Promise.resolve({}) }));

    const table = screen.getByRole("table");
    const cards = screen.getByTestId("mobile-cards");

    expect(within(table).getAllByText(/not enrolled/i)).toHaveLength(1);
    expect(within(cards).getAllByText(/not enrolled/i)).toHaveLength(1);
  });
```

- [ ] **Step 2: Run the test to verify it fails**

Run (from `portal/`): `npm test -- employees/page.test.tsx`
Expected: FAIL — no "Not Enrolled" text exists anywhere yet.

- [ ] **Step 3: Extend the EmployeeResponse type**

In `portal/src/lib/types.ts`, replace:

```ts
export type EmployeeResponse = {
  id: string;
  employeeCode: string;
  name: string;
  shiftId: string | null;
};
```

with:

```ts
export type EmployeeResponse = {
  id: string;
  employeeCode: string;
  name: string;
  shiftId: string | null;
  hasFingerprint: boolean;
};
```

- [ ] **Step 4: Render the badge**

In `portal/src/app/(dashboard)/employees/page.tsx`, add the import (alongside the existing imports at the top):

```tsx
import { Badge } from "../Badge";
```

In the desktop table's `<thead>`, add a new column header between "Shift" and the blank actions header — replace:

```tsx
              <th className="py-3 px-4 font-medium">Shift</th>
              <th className="py-3 px-4"></th>
```

with:

```tsx
              <th className="py-3 px-4 font-medium">Shift</th>
              <th className="py-3 px-4 font-medium">Fingerprint</th>
              <th className="py-3 px-4"></th>
```

In the desktop table's row rendering, add a matching cell — replace:

```tsx
                <td className="py-3 px-4">{shifts.find((s) => s.id === employee.shiftId)?.name ?? "—"}</td>
                <td className="py-3 px-4">
                  <div className="flex gap-3 items-center">
                    <Link href={`/employees?edit=${employee.id}`} className="text-ink hover:text-accent text-sm">
                      Edit
                    </Link>
                    <DeleteEmployeeButton id={employee.id} name={employee.name} />
                  </div>
                </td>
```

with:

```tsx
                <td className="py-3 px-4">{shifts.find((s) => s.id === employee.shiftId)?.name ?? "—"}</td>
                <td className="py-3 px-4">{!employee.hasFingerprint && <Badge>Not Enrolled</Badge>}</td>
                <td className="py-3 px-4">
                  <div className="flex gap-3 items-center">
                    <Link href={`/employees?edit=${employee.id}`} className="text-ink hover:text-accent text-sm">
                      Edit
                    </Link>
                    <DeleteEmployeeButton id={employee.id} name={employee.name} />
                  </div>
                </td>
```

In the mobile card block, add the badge next to the existing code/shift line — replace:

```tsx
            <div>
              <div className="font-medium text-ink">{employee.name}</div>
              <div className="text-xs text-ink-soft mt-1">
                {employee.employeeCode} · {shifts.find((s) => s.id === employee.shiftId)?.name ?? "—"}
              </div>
            </div>
```

with:

```tsx
            <div>
              <div className="font-medium text-ink">{employee.name}</div>
              <div className="text-xs text-ink-soft mt-1 flex items-center gap-1 flex-wrap">
                <span>
                  {employee.employeeCode} · {shifts.find((s) => s.id === employee.shiftId)?.name ?? "—"}
                </span>
                {!employee.hasFingerprint && <Badge>Not Enrolled</Badge>}
              </div>
            </div>
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `npm test -- employees/page.test.tsx`
Expected: PASS (all tests in the file — existing + 1 new)

- [ ] **Step 6: Run the full portal test suite**

Run: `npm test`
Expected: PASS (no regressions)

- [ ] **Step 7: Manually verify in the browser**

Run: `npm run build && npm run start` (backend must be running). On the Employees page, confirm the "Fingerprint" column (desktop) and the inline badge (mobile, resize below `md`) show "Not Enrolled" only for employees without an enrolled template, and nothing extra for enrolled ones.

- [ ] **Step 8: Commit**

```bash
git add portal/src/lib/types.ts "portal/src/app/(dashboard)/employees/page.tsx" "portal/src/app/(dashboard)/employees/page.test.tsx"
git commit -m "feat: show a Not Enrolled badge for employees missing a fingerprint template"
```
