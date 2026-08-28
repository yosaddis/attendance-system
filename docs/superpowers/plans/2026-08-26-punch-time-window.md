# Punch Time Window (±30 Minutes) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reject a kiosk punch that isn't within 30 minutes of the matching shift time (start for In, end for Out, break start/end for Break Out/Break In), before fingerprint capture ever starts.

**Architecture:** Backend's employee-lookup response gains the employee's shift times; the agent's employee cache carries them through to the desktop kiosk; `PunchCaptureService` checks the window immediately after resolving the employee, before touching the device.

**Tech Stack:** ASP.NET Core 8 (backend), WPF / .NET 8 with EF Core + SQLite (agent), xUnit on both sides.

## Global Constraints

- Window is inclusive: a punch exactly on either boundary is accepted.
- No shift, or no configured time for the requested punch type (e.g. Break Out/In on a shift with no `BreakStart`/`BreakEnd`), means unrestricted — never reject when there's nothing to compare against.
- No timezone conversion — the kiosk's own clock (`DateTime.Now`) is already local time.
- Overnight shifts (end time earlier than start time) are explicitly out of scope — not handled, matches an existing limitation elsewhere in this codebase.
- No admin override exists or is being added.
- Rejection message format: `"Too {early|late} to punch {Label} — accepted from {start} to {end}."`, where `{Label}` is `In`, `Out`, `Break Out`, or `Break In`, and times are formatted 12-hour with AM/PM (e.g. `8:30 AM`).
- **Test design note:** this codebase has no injectable clock anywhere (`PunchCaptureService` already calls `DateTimeOffset.UtcNow` directly) — adding one is out of scope. Tests for the window logic therefore compute shift times relative to the real `DateTime.Now` at test-run time (e.g. "shift starts 45 minutes from now" to test the too-early case), rather than using fixed clock times. Exact-boundary-instant tests (precisely 30:00 before/after) are deliberately skipped as inherently flaky against a real, un-mocked clock — near-boundary values (e.g. 20 minutes inside, 45 minutes outside) are used instead to keep results deterministic while still exercising both sides of the window.

---

### Task 1: Backend — shift times on employee lookup

**Files:**
- Modify: `backend/src/AttendanceApi/Dtos/EmployeeDtos.cs`
- Modify: `backend/src/AttendanceApi/Controllers/EmployeeLookupController.cs`
- Modify: `backend/tests/AttendanceApi.Tests/EmployeeLookupControllerTests.cs`

**Interfaces:**
- Produces: `EmployeeLookupResponse(Guid EmployeeId, string EmployeeCode, string Name, TimeOnly? ShiftStartTime = null, TimeOnly? ShiftEndTime = null, TimeOnly? ShiftBreakStart = null, TimeOnly? ShiftBreakEnd = null)`.

- [ ] **Step 1: Write the failing tests**

Add a new seeding helper and 3 new tests to `backend/tests/AttendanceApi.Tests/EmployeeLookupControllerTests.cs`. Add the helper right after the existing `SeedTenantEmployeeAndStation` method:

```csharp
    private string SeedTenantEmployeeStationAndShift(
        TimeOnly start, TimeOnly end, TimeOnly? breakStart, TimeOnly? breakEnd, out Guid employeeId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = new Tenant { Name = "Acme Foods", DeviceVendor = DeviceVendor.Zk4500 };
        var shift = new Shift
        {
            TenantId = tenant.Id,
            Name = "Day Shift",
            StartTime = start,
            EndTime = end,
            GraceMinutes = 10,
            BreakStart = breakStart,
            BreakEnd = breakEnd,
        };
        var employee = new Employee { TenantId = tenant.Id, EmployeeCode = "E001", Name = "Jane Doe", ShiftId = shift.Id };
        var (plaintextKey, hash) = StationKeyGenerator.Generate();
        var station = new Station { TenantId = tenant.Id, Name = "Front Desk", DeviceVendor = DeviceVendor.Zk4500, ApiKeyHash = hash };
        db.Tenants.Add(tenant);
        db.Shifts.Add(shift);
        db.Employees.Add(employee);
        db.Stations.Add(station);
        db.SaveChanges();
        employeeId = employee.Id;
        return plaintextKey;
    }
```

Add these 3 tests at the end of the class, before the closing brace:

```csharp
    [Fact]
    public async Task Lookup_EmployeeWithShift_ReturnsShiftStartEndAndBreakTimes()
    {
        var stationKey = SeedTenantEmployeeStationAndShift(
            new TimeOnly(9, 0), new TimeOnly(17, 0), new TimeOnly(12, 0), new TimeOnly(13, 0), out _);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Station-Key", stationKey);

        var response = await client.GetAsync("/api/employees/lookup?code=E001");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<EmployeeLookupResponse>();
        Assert.Equal(new TimeOnly(9, 0), body!.ShiftStartTime);
        Assert.Equal(new TimeOnly(17, 0), body.ShiftEndTime);
        Assert.Equal(new TimeOnly(12, 0), body.ShiftBreakStart);
        Assert.Equal(new TimeOnly(13, 0), body.ShiftBreakEnd);
    }

    [Fact]
    public async Task Lookup_EmployeeWithShiftButNoBreakTimes_ReturnsNullBreakTimes()
    {
        var stationKey = SeedTenantEmployeeStationAndShift(new TimeOnly(9, 0), new TimeOnly(17, 0), null, null, out _);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Station-Key", stationKey);

        var response = await client.GetAsync("/api/employees/lookup?code=E001");

        var body = await response.Content.ReadFromJsonAsync<EmployeeLookupResponse>();
        Assert.Equal(new TimeOnly(9, 0), body!.ShiftStartTime);
        Assert.Equal(new TimeOnly(17, 0), body.ShiftEndTime);
        Assert.Null(body.ShiftBreakStart);
        Assert.Null(body.ShiftBreakEnd);
    }

    [Fact]
    public async Task Lookup_EmployeeWithNoShift_ReturnsAllNullShiftFields()
    {
        var stationKey = SeedTenantEmployeeAndStation(out _);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Station-Key", stationKey);

        var response = await client.GetAsync("/api/employees/lookup?code=E001");

        var body = await response.Content.ReadFromJsonAsync<EmployeeLookupResponse>();
        Assert.Null(body!.ShiftStartTime);
        Assert.Null(body.ShiftEndTime);
        Assert.Null(body.ShiftBreakStart);
        Assert.Null(body.ShiftBreakEnd);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run (from `backend/`): `dotnet test --filter EmployeeLookupControllerTests`
Expected: FAIL — `EmployeeLookupResponse` has no `ShiftStartTime`/etc. members yet (compile error).

- [ ] **Step 3: Extend EmployeeLookupResponse**

In `backend/src/AttendanceApi/Dtos/EmployeeDtos.cs`, replace:

```csharp
public record EmployeeLookupResponse(Guid EmployeeId, string EmployeeCode, string Name);
```

with:

```csharp
public record EmployeeLookupResponse(
    Guid EmployeeId,
    string EmployeeCode,
    string Name,
    TimeOnly? ShiftStartTime = null,
    TimeOnly? ShiftEndTime = null,
    TimeOnly? ShiftBreakStart = null,
    TimeOnly? ShiftBreakEnd = null);
```

- [ ] **Step 4: Populate the new fields in EmployeeLookupController**

In `backend/src/AttendanceApi/Controllers/EmployeeLookupController.cs`, replace:

```csharp
        var employee = await _db.Employees.SingleOrDefaultAsync(e => e.TenantId == tenantId && e.EmployeeCode == code);
        if (employee is null) return NotFound();

        return new EmployeeLookupResponse(employee.Id, employee.EmployeeCode, employee.Name);
```

with:

```csharp
        var employee = await _db.Employees.SingleOrDefaultAsync(e => e.TenantId == tenantId && e.EmployeeCode == code);
        if (employee is null) return NotFound();

        var shift = employee.ShiftId is null
            ? null
            : await _db.Shifts.SingleOrDefaultAsync(s => s.Id == employee.ShiftId);

        return new EmployeeLookupResponse(
            employee.Id,
            employee.EmployeeCode,
            employee.Name,
            shift?.StartTime,
            shift?.EndTime,
            shift?.BreakStart,
            shift?.BreakEnd);
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test --filter EmployeeLookupControllerTests`
Expected: PASS (all 8 tests — 5 existing + 3 new)

- [ ] **Step 6: Commit**

```bash
git add backend/src/AttendanceApi/Dtos/EmployeeDtos.cs backend/src/AttendanceApi/Controllers/EmployeeLookupController.cs backend/tests/AttendanceApi.Tests/EmployeeLookupControllerTests.cs
git commit -m "feat: include shift start/end/break times on employee lookup"
```

---

### Task 2: Agent — carry shift times through the employee cache

**Files:**
- Modify: `agent/src/AttendanceAgent/Api/ApiDtos.cs`
- Modify: `agent/src/AttendanceAgent/Data/CachedEmployee.cs`
- Modify: `agent/src/AttendanceAgent/Services/EmployeeDirectoryService.cs`
- Modify: `agent/tests/AttendanceAgent.Tests/EmployeeDirectoryServiceTests.cs`

**Interfaces:**
- Consumes: the backend's `EmployeeLookupResponse` shape from Task 1 (field names must match exactly — `BackendApiClient.LookupEmployeeAsync` deserializes the JSON response directly into this task's `EmployeeLookupResult`, case-insensitively).
- Produces: `EmployeeLookupResult(Guid EmployeeId, string EmployeeCode, string Name, TimeOnly? ShiftStartTime = null, TimeOnly? ShiftEndTime = null, TimeOnly? ShiftBreakStart = null, TimeOnly? ShiftBreakEnd = null)` — the four new fields are read by Task 3.

- [ ] **Step 1: Write the failing tests**

Add these 2 tests to `agent/tests/AttendanceAgent.Tests/EmployeeDirectoryServiceTests.cs`, right after `ResolveAsync_Online_CachesResultLocally`:

```csharp
    [Fact]
    public async Task ResolveAsync_Online_CachesShiftTimesLocally()
    {
        using var db = TestDb.CreateInMemory();
        var api = new FakeBackendApiClient
        {
            LookupResult = new EmployeeLookupResult(
                EmployeeId, "E001", "Jane Doe",
                new TimeOnly(9, 0), new TimeOnly(17, 0), new TimeOnly(12, 0), new TimeOnly(13, 0)),
        };
        var service = new EmployeeDirectoryService(api, db, NullLogger<EmployeeDirectoryService>.Instance);

        await service.ResolveAsync("E001");

        var cached = await db.CachedEmployees.FindAsync(EmployeeId);
        Assert.Equal(new TimeOnly(9, 0), cached!.ShiftStartTime);
        Assert.Equal(new TimeOnly(17, 0), cached.ShiftEndTime);
        Assert.Equal(new TimeOnly(12, 0), cached.ShiftBreakStart);
        Assert.Equal(new TimeOnly(13, 0), cached.ShiftBreakEnd);
    }

    [Fact]
    public async Task ResolveAsync_Offline_FallsBackToCachedShiftTimes()
    {
        using var db = TestDb.CreateInMemory();
        db.CachedEmployees.Add(new CachedEmployee
        {
            EmployeeId = EmployeeId,
            EmployeeCode = "E001",
            Name = "Jane Doe",
            ShiftStartTime = new TimeOnly(9, 0),
            ShiftEndTime = new TimeOnly(17, 0),
            CachedAt = DateTimeOffset.UtcNow,
        });
        db.SaveChanges();
        var api = new FakeBackendApiClient { ThrowOnLookup = true };
        var service = new EmployeeDirectoryService(api, db, NullLogger<EmployeeDirectoryService>.Instance);

        var result = await service.ResolveAsync("E001");

        Assert.Equal(new TimeOnly(9, 0), result!.ShiftStartTime);
        Assert.Equal(new TimeOnly(17, 0), result.ShiftEndTime);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run (from `agent/`): `dotnet test --filter EmployeeDirectoryServiceTests`
Expected: FAIL — `EmployeeLookupResult` has no `ShiftStartTime`/etc. constructor parameters yet (compile error).

- [ ] **Step 3: Extend EmployeeLookupResult**

In `agent/src/AttendanceAgent/Api/ApiDtos.cs`, replace:

```csharp
public record EmployeeLookupResult(Guid EmployeeId, string EmployeeCode, string Name);
```

with:

```csharp
public record EmployeeLookupResult(
    Guid EmployeeId,
    string EmployeeCode,
    string Name,
    TimeOnly? ShiftStartTime = null,
    TimeOnly? ShiftEndTime = null,
    TimeOnly? ShiftBreakStart = null,
    TimeOnly? ShiftBreakEnd = null);
```

- [ ] **Step 4: Extend CachedEmployee**

Replace the full contents of `agent/src/AttendanceAgent/Data/CachedEmployee.cs`:

```csharp
namespace AttendanceAgent.Data;

public class CachedEmployee
{
    public Guid EmployeeId { get; set; }
    public required string EmployeeCode { get; set; }
    public required string Name { get; set; }
    public TimeOnly? ShiftStartTime { get; set; }
    public TimeOnly? ShiftEndTime { get; set; }
    public TimeOnly? ShiftBreakStart { get; set; }
    public TimeOnly? ShiftBreakEnd { get; set; }
    public DateTimeOffset CachedAt { get; set; }
}
```

- [ ] **Step 5: Carry the fields through EmployeeDirectoryService**

In `agent/src/AttendanceAgent/Services/EmployeeDirectoryService.cs`, replace the offline-fallback return (currently the last line of `ResolveAsync`):

```csharp
        var cached = await _db.CachedEmployees.SingleOrDefaultAsync(e => e.EmployeeCode == code, ct);
        return cached is null ? null : new EmployeeLookupResult(cached.EmployeeId, cached.EmployeeCode, cached.Name);
```

with:

```csharp
        var cached = await _db.CachedEmployees.SingleOrDefaultAsync(e => e.EmployeeCode == code, ct);
        return cached is null ? null : new EmployeeLookupResult(
            cached.EmployeeId, cached.EmployeeCode, cached.Name,
            cached.ShiftStartTime, cached.ShiftEndTime, cached.ShiftBreakStart, cached.ShiftBreakEnd);
```

Replace the insert branch of `UpsertCacheAsync`:

```csharp
            _db.CachedEmployees.Add(new CachedEmployee
            {
                EmployeeId = result.EmployeeId,
                EmployeeCode = result.EmployeeCode,
                Name = result.Name,
                CachedAt = DateTimeOffset.UtcNow,
            });
```

with:

```csharp
            _db.CachedEmployees.Add(new CachedEmployee
            {
                EmployeeId = result.EmployeeId,
                EmployeeCode = result.EmployeeCode,
                Name = result.Name,
                ShiftStartTime = result.ShiftStartTime,
                ShiftEndTime = result.ShiftEndTime,
                ShiftBreakStart = result.ShiftBreakStart,
                ShiftBreakEnd = result.ShiftBreakEnd,
                CachedAt = DateTimeOffset.UtcNow,
            });
```

Replace the update branch of `UpsertCacheAsync`:

```csharp
        else
        {
            existing.EmployeeCode = result.EmployeeCode;
            existing.Name = result.Name;
            existing.CachedAt = DateTimeOffset.UtcNow;
        }
```

with:

```csharp
        else
        {
            existing.EmployeeCode = result.EmployeeCode;
            existing.Name = result.Name;
            existing.ShiftStartTime = result.ShiftStartTime;
            existing.ShiftEndTime = result.ShiftEndTime;
            existing.ShiftBreakStart = result.ShiftBreakStart;
            existing.ShiftBreakEnd = result.ShiftBreakEnd;
            existing.CachedAt = DateTimeOffset.UtcNow;
        }
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test --filter EmployeeDirectoryServiceTests`
Expected: PASS (all tests — existing + 2 new)

- [ ] **Step 7: Run the full agent test suite**

Run: `dotnet test`
Expected: PASS (no regressions — `EmployeeLookupResult`'s new fields are optional, so every existing 3-arg call site across the test project still compiles)

- [ ] **Step 8: Commit**

```bash
git add agent/src/AttendanceAgent/Api/ApiDtos.cs agent/src/AttendanceAgent/Data/CachedEmployee.cs agent/src/AttendanceAgent/Services/EmployeeDirectoryService.cs agent/tests/AttendanceAgent.Tests/EmployeeDirectoryServiceTests.cs
git commit -m "feat: cache shift start/end/break times with each employee lookup"
```

---

### Task 3: Agent — enforce the punch window before capture

**Files:**
- Modify: `agent/src/AttendanceAgent/Services/PunchCaptureService.cs`
- Modify: `agent/tests/AttendanceAgent.Tests/PunchCaptureServiceTests.cs`

**Interfaces:**
- Consumes: `EmployeeLookupResult.ShiftStartTime/ShiftEndTime/ShiftBreakStart/ShiftBreakEnd` from Task 2.

- [ ] **Step 1: Write the failing tests**

Add these 4 tests to `agent/tests/AttendanceAgent.Tests/PunchCaptureServiceTests.cs`, right after `CapturePunch_MatchingFingerprint_EnqueuesPunch`:

```csharp
    [Fact]
    public async Task CapturePunch_WithinShiftStartWindow_Succeeds()
    {
        using var db = TestDb.CreateInMemory();
        var employeeId = Guid.NewGuid();
        var shiftStart = TimeOnly.FromDateTime(DateTime.Now.AddMinutes(-20));
        var api = new FakeBackendApiClient
        {
            LookupResult = new EmployeeLookupResult(employeeId, "E001", "Jane Doe", ShiftStartTime: shiftStart),
            TemplateResult = new byte[] { 1, 2, 3 },
        };
        var queue = new PunchQueueService(db);
        var service = new PunchCaptureService(
            new EmployeeDirectoryService(api, db, NullLogger<EmployeeDirectoryService>.Instance),
            new TemplateCacheService(api, db, NullLogger<TemplateCacheService>.Instance),
            new FakeFingerprintVerifier { AlwaysMatches = true },
            queue);
        var device = new FakeFingerprintDevice { NextCapture = new byte[] { 9, 9 } };

        var result = await service.CapturePunchAsync("E001", "In", device);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task CapturePunch_BeforeShiftStartWindow_FailsWithoutTouchingDevice()
    {
        using var db = TestDb.CreateInMemory();
        var employeeId = Guid.NewGuid();
        var shiftStart = TimeOnly.FromDateTime(DateTime.Now.AddMinutes(45));
        var api = new FakeBackendApiClient
        {
            LookupResult = new EmployeeLookupResult(employeeId, "E001", "Jane Doe", ShiftStartTime: shiftStart),
            TemplateResult = new byte[] { 1, 2, 3 },
        };
        var queue = new PunchQueueService(db);
        var service = new PunchCaptureService(
            new EmployeeDirectoryService(api, db, NullLogger<EmployeeDirectoryService>.Instance),
            new TemplateCacheService(api, db, NullLogger<TemplateCacheService>.Instance),
            new FakeFingerprintVerifier { AlwaysMatches = true },
            queue);
        var device = new FakeFingerprintDevice { NextCapture = new byte[] { 9, 9 } };

        var result = await service.CapturePunchAsync("E001", "In", device);

        Assert.False(result.Success);
        Assert.Contains("Too early", result.Message);
        Assert.False(device.IsAcquired);
        Assert.Empty(await queue.GetPendingAsync());
    }

    [Fact]
    public async Task CapturePunch_AfterShiftEndWindow_FailsWithoutTouchingDevice()
    {
        using var db = TestDb.CreateInMemory();
        var employeeId = Guid.NewGuid();
        var shiftEnd = TimeOnly.FromDateTime(DateTime.Now.AddMinutes(-45));
        var api = new FakeBackendApiClient
        {
            LookupResult = new EmployeeLookupResult(employeeId, "E001", "Jane Doe", ShiftEndTime: shiftEnd),
            TemplateResult = new byte[] { 1, 2, 3 },
        };
        var queue = new PunchQueueService(db);
        var service = new PunchCaptureService(
            new EmployeeDirectoryService(api, db, NullLogger<EmployeeDirectoryService>.Instance),
            new TemplateCacheService(api, db, NullLogger<TemplateCacheService>.Instance),
            new FakeFingerprintVerifier { AlwaysMatches = true },
            queue);
        var device = new FakeFingerprintDevice { NextCapture = new byte[] { 9, 9 } };

        var result = await service.CapturePunchAsync("E001", "Out", device);

        Assert.False(result.Success);
        Assert.Contains("Too late", result.Message);
        Assert.False(device.IsAcquired);
        Assert.Empty(await queue.GetPendingAsync());
    }

    [Fact]
    public async Task CapturePunch_BreakPunchWithNoConfiguredBreakTimes_IsUnrestricted()
    {
        using var db = TestDb.CreateInMemory();
        var employeeId = Guid.NewGuid();
        var api = new FakeBackendApiClient
        {
            LookupResult = new EmployeeLookupResult(
                employeeId, "E001", "Jane Doe", ShiftStartTime: new TimeOnly(9, 0), ShiftEndTime: new TimeOnly(17, 0)),
            TemplateResult = new byte[] { 1, 2, 3 },
        };
        var queue = new PunchQueueService(db);
        var service = new PunchCaptureService(
            new EmployeeDirectoryService(api, db, NullLogger<EmployeeDirectoryService>.Instance),
            new TemplateCacheService(api, db, NullLogger<TemplateCacheService>.Instance),
            new FakeFingerprintVerifier { AlwaysMatches = true },
            queue);
        var device = new FakeFingerprintDevice { NextCapture = new byte[] { 9, 9 } };

        var result = await service.CapturePunchAsync("E001", "BreakOut", device);

        Assert.True(result.Success);
    }
```

Note: `CapturePunch_MatchingFingerprint_EnqueuesPunch` (existing, unmodified) already proves the no-shift case stays unrestricted — it constructs `EmployeeLookupResult(employeeId, "E001", "Jane Doe")` with no shift times and must keep passing.

- [ ] **Step 2: Run the tests to verify they fail**

Run (from `agent/`): `dotnet test --filter PunchCaptureServiceTests`
Expected: FAIL — `CapturePunch_WithinShiftStartWindow_Succeeds` passes (nothing rejects it yet) but `CapturePunch_BeforeShiftStartWindow_FailsWithoutTouchingDevice` and `CapturePunch_AfterShiftEndWindow_FailsWithoutTouchingDevice` FAIL (`result.Success` is `true`, no rejection happens yet).

- [ ] **Step 3: Add the window check to PunchCaptureService**

In `agent/src/AttendanceAgent/Services/PunchCaptureService.cs`, replace:

```csharp
    public async Task<PunchResult> CapturePunchAsync(string employeeCode, string punchType, IFingerprintDevice device, CancellationToken ct = default)
    {
        var employee = await _employees.ResolveAsync(employeeCode, ct);
        if (employee is null)
            return new PunchResult(false, $"Employee code '{employeeCode}' not recognized.");

        var enrolledTemplate = await _templates.GetTemplateAsync(employee.EmployeeId, ct);
```

with:

```csharp
    private const int PunchWindowMarginMinutes = 30;

    public async Task<PunchResult> CapturePunchAsync(string employeeCode, string punchType, IFingerprintDevice device, CancellationToken ct = default)
    {
        var employee = await _employees.ResolveAsync(employeeCode, ct);
        if (employee is null)
            return new PunchResult(false, $"Employee code '{employeeCode}' not recognized.");

        var windowRejection = CheckPunchWindow(punchType, employee);
        if (windowRejection is not null)
            return new PunchResult(false, windowRejection);

        var enrolledTemplate = await _templates.GetTemplateAsync(employee.EmployeeId, ct);
```

> **Amended during implementation:** the original version of this step used raw `TimeSpan`
> arithmetic (`DateTime.Now.TimeOfDay` minus/plus a `TimeSpan` margin), which breaks whenever a
> shift boundary sits within 30 minutes of midnight and `DateTime.Now` falls on the other side of
> it — e.g. shift ends 23:50, now is 00:10; the naive difference reads as ~23h40m instead of the
> real 20 minutes, misclassifying an in-window punch as "too early." This was caught live: Task
> 3's own tests (which compute shift times relative to the real clock, per this plan's Global
> Constraints) started failing right as the session crossed midnight into the next day, landing
> exactly on this case by chance. The version below replaces the `TimeSpan` comparison with signed,
> wrapped minute-of-day arithmetic, which handles midnight correctly without needing to solve the
> broader (still out-of-scope) overnight-shift problem — this only wraps the ±30 minute window
> itself, not shift start/end ordering.

Add this private static method at the end of the `PunchCaptureService` class, just before its closing brace:

```csharp
    // Returns a rejection message if `now` falls outside the ±30 minute window around the shift
    // time matching this punch type, or null if the punch is allowed — including when the
    // employee has no shift, or the shift has no configured time for this punch type (e.g. no
    // BreakStart/BreakEnd on a 2-punch shift). Nothing to compare against means nothing to enforce.
    private static string? CheckPunchWindow(string punchType, EmployeeLookupResult employee)
    {
        var (shiftTime, punchLabel) = punchType switch
        {
            "In" => (employee.ShiftStartTime, "In"),
            "Out" => (employee.ShiftEndTime, "Out"),
            "BreakOut" => (employee.ShiftBreakStart, "Break Out"),
            "BreakIn" => (employee.ShiftBreakEnd, "Break In"),
            _ => ((TimeOnly?)null, punchType),
        };

        if (shiftTime is null) return null;

        // Signed shortest-path distance (in minutes) from the shift time to now, wrapping
        // correctly around midnight. Raw, unwrapped time-of-day arithmetic (comparing TimeSpans
        // directly) miscompares whenever a shift boundary sits within the margin of midnight and
        // "now" falls on the other side of it — e.g. shift ends 23:50, now is 00:10; the naive
        // difference looks like ~23h40m instead of the real 20 minutes.
        var now = TimeOnly.FromDateTime(DateTime.Now);
        var diffMinutes = (now.Hour * 60 + now.Minute) - (shiftTime.Value.Hour * 60 + shiftTime.Value.Minute);
        if (diffMinutes > 720) diffMinutes -= 1440;
        if (diffMinutes <= -720) diffMinutes += 1440;

        if (Math.Abs(diffMinutes) <= PunchWindowMarginMinutes) return null;

        var tooEarly = diffMinutes < 0;
        var windowStart = shiftTime.Value.AddMinutes(-PunchWindowMarginMinutes).ToString("h:mm tt");
        var windowEnd = shiftTime.Value.AddMinutes(PunchWindowMarginMinutes).ToString("h:mm tt");
        return $"Too {(tooEarly ? "early" : "late")} to punch {punchLabel} — accepted from {windowStart} to {windowEnd}.";
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter PunchCaptureServiceTests`
Expected: PASS (all tests — existing + 4 new)

- [ ] **Step 5: Run the full agent test suite**

Run: `dotnet test`
Expected: PASS (no regressions)

- [ ] **Step 6: Manually verify in the running app**

Run: `dotnet run --project src/AttendanceAgent -c Release -p:DeviceVendor=Zk4500` (or `SecuGen`, whichever hardware is attached — Debug builds always use the fake device per this session's earlier finding, so a manual check needs Release+DeviceVendor to mean anything). With an employee assigned to a shift whose start time is more than 30 minutes from now, attempt an "IN" punch and confirm the kiosk shows the "Too early"/"Too late" message immediately, with no fingerprint-capture prompt.

- [ ] **Step 7: Commit**

```bash
git add agent/src/AttendanceAgent/Services/PunchCaptureService.cs agent/tests/AttendanceAgent.Tests/PunchCaptureServiceTests.cs
git commit -m "feat: reject punches outside a 30-minute window around the matching shift time"
```
