# Attendance Analytics, Dashboard & Reports Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a late/missing-checkout/double-punch/worked-hours analysis engine to the backend, expose it through three endpoints, and surface it in the portal as a tenant dashboard, an enriched daily attendance view, and a date-range report with CSV export.

**Architecture:** A pure, DB-free `AttendanceAnalysisService` computes one employee-day's analysis from that day's punches plus their (possibly null) `Shift`. `AttendanceController` builds the per-employee-day rows (querying `Employees` union `Punches`' distinct employee ids, so both "no punches at all" and "punches from an employee id outside this tenant" are represented, matching current defensive behavior) and calls the service once per employee. Three portal pages consume the three new/extended endpoints.

**Tech Stack:** ASP.NET Core 8 (C#) backend with EF Core/SQLite-in-tests, xUnit; Next.js 16 / React 19 portal with Vitest + Testing Library, Tailwind v4 design tokens already defined in `portal/src/app/globals.css`.

## Global Constraints

- Day boundaries and time-of-day comparisons use a fixed `DefaultTenantOffset = TimeSpan.FromHours(3)` (GMT+3) — the *only* place this value is defined; a future per-tenant timezone field replaces just this one constant.
- Employees with no `ShiftId` are excluded from late/absent/present counts (no baseline to compare against) but still appear in the daily/report rows with `HasShift: false`.
- "Missing checkout" = the day's last punch (by timestamp) is not `Out`.
- "Double punch" = two punches of the same `PunchType` for the same employee within 5 minutes of each other.
- FourPunch worked hours = `(BreakOut − In) + (Out − BreakIn)`, using each type's first chronological occurrence that day; if any of the four types is absent that day, worked hours is `null` for that day.
- TwoPunch (and no-shift) worked hours = sum of each completed `In → Out` pair in chronological order; an unpaired trailing `In` contributes nothing.
- The date-range report/export is capped at 90 days inclusive; `to < from` or a longer range returns `400 Bad Request`.
- Export format is CSV only, `text/csv`, `Content-Disposition: attachment`.
- Badges (Late / Missing Checkout / Double Punch) render only when true — no "OK" badge for the normal case.

---

### Task 1: AttendanceAnalysisService (pure computation engine)

**Files:**
- Create: `backend/src/AttendanceApi/Services/AttendanceAnalysisService.cs`
- Test: `backend/tests/AttendanceApi.Tests/AttendanceAnalysisServiceTests.cs`

**Interfaces:**
- Produces: `AttendanceAnalysisService.DefaultTenantOffset` (`TimeSpan`), `AttendanceAnalysisService.Analyze(List<Punch> dayPunches, Shift? shift)` returning `AttendanceAnalysisResult(DateTimeOffset? FirstIn, DateTimeOffset? LastOut, double? WorkedHours, bool HasShift, bool IsLate, int? LateMinutes, bool IsMissingCheckout, bool HasDoublePunch)` — every later backend task calls this exact method with this exact signature.

- [ ] **Step 1: Write the failing tests**

```csharp
// backend/tests/AttendanceApi.Tests/AttendanceAnalysisServiceTests.cs
using AttendanceApi.Entities;
using AttendanceApi.Services;
using Xunit;

namespace AttendanceApi.Tests;

public class AttendanceAnalysisServiceTests
{
    private static Punch P(PunchType type, int hour, int minute = 0, int day = 10) =>
        new()
        {
            Id = Guid.NewGuid(),
            PunchType = type,
            Timestamp = new DateTimeOffset(2026, 8, day, hour, minute, 0, TimeSpan.Zero),
        };

    private static Shift TwoPunchShift(TimeOnly start, int grace = 0) => new()
    {
        Name = "Day",
        StartTime = start,
        EndTime = new TimeOnly(17, 0),
        GraceMinutes = grace,
        PunchMode = PunchMode.TwoPunch,
    };

    private static Shift FourPunchShift(TimeOnly start, int grace = 0) => new()
    {
        Name = "Day",
        StartTime = start,
        EndTime = new TimeOnly(17, 0),
        GraceMinutes = grace,
        PunchMode = PunchMode.FourPunch,
    };

    [Fact]
    public void NoShift_NeverComputesLate()
    {
        // 09:00 UTC = 12:00 local (GMT+3) — would be "late" against an 08:00 shift, but there's no shift.
        var result = AttendanceAnalysisService.Analyze(new List<Punch> { P(PunchType.In, 9) }, null);

        Assert.False(result.HasShift);
        Assert.False(result.IsLate);
        Assert.Null(result.LateMinutes);
    }

    [Fact]
    public void WithShift_OnTimeWithinGrace_IsNotLate()
    {
        // Shift starts 05:00 local with 10 min grace. Punch at 06:00 UTC = 09:00 local... use a
        // shift start that makes the local arrival land inside grace.
        var shift = TwoPunchShift(new TimeOnly(9, 5), grace: 10);
        // 06:00 UTC + 3h offset = 09:00 local, which is before 09:05 + 10min grace (09:15).
        var result = AttendanceAnalysisService.Analyze(new List<Punch> { P(PunchType.In, 6) }, shift);

        Assert.True(result.HasShift);
        Assert.False(result.IsLate);
        Assert.Null(result.LateMinutes);
    }

    [Fact]
    public void WithShift_ArrivalPastGrace_IsLateWithMinutes()
    {
        // Shift starts 08:00 local, 5 min grace (threshold 08:05 local). Punch at 06:20 UTC = 09:20 local.
        var shift = TwoPunchShift(new TimeOnly(8, 0), grace: 5);
        var result = AttendanceAnalysisService.Analyze(new List<Punch> { P(PunchType.In, 6, 20) }, shift);

        Assert.True(result.IsLate);
        Assert.Equal(75, result.LateMinutes); // 09:20 local - 08:05 threshold = 75 minutes
    }

    [Fact]
    public void AbsentDay_NoPunchesAtAll_IsNotLateAndHasNoWorkedHours()
    {
        var shift = TwoPunchShift(new TimeOnly(8, 0));
        var result = AttendanceAnalysisService.Analyze(new List<Punch>(), shift);

        Assert.Null(result.FirstIn);
        Assert.Null(result.LastOut);
        Assert.Null(result.WorkedHours);
        Assert.False(result.IsLate);
        Assert.False(result.IsMissingCheckout);
    }

    [Fact]
    public void LastPunchIsIn_IsMissingCheckout()
    {
        var result = AttendanceAnalysisService.Analyze(
            new List<Punch> { P(PunchType.In, 9), P(PunchType.Out, 13), P(PunchType.In, 14) }, null);

        Assert.True(result.IsMissingCheckout);
    }

    [Fact]
    public void LastPunchIsOut_IsNotMissingCheckout()
    {
        var result = AttendanceAnalysisService.Analyze(
            new List<Punch> { P(PunchType.In, 9), P(PunchType.Out, 17) }, null);

        Assert.False(result.IsMissingCheckout);
    }

    [Fact]
    public void TwoPunchesOfSameTypeWithinFiveMinutes_IsDoublePunch()
    {
        var result = AttendanceAnalysisService.Analyze(
            new List<Punch> { P(PunchType.In, 9, 0), P(PunchType.In, 9, 3) }, null);

        Assert.True(result.HasDoublePunch);
    }

    [Fact]
    public void TwoPunchesOfSameTypeSixMinutesApart_IsNotDoublePunch()
    {
        var result = AttendanceAnalysisService.Analyze(
            new List<Punch> { P(PunchType.In, 9, 0), P(PunchType.In, 9, 6) }, null);

        Assert.False(result.HasDoublePunch);
    }

    [Fact]
    public void TwoPunchMode_SumsCompletedInOutPairs()
    {
        var shift = TwoPunchShift(new TimeOnly(8, 0));
        var result = AttendanceAnalysisService.Analyze(
            new List<Punch> { P(PunchType.In, 9), P(PunchType.Out, 17) }, shift);

        Assert.Equal(8.0, result.WorkedHours);
    }

    [Fact]
    public void TwoPunchMode_DuplicateInIgnoredForWorkedHours()
    {
        var shift = TwoPunchShift(new TimeOnly(8, 0));
        // Double-punched In at 09:03 must not reset the open "In" from 09:00.
        var result = AttendanceAnalysisService.Analyze(
            new List<Punch> { P(PunchType.In, 9, 0), P(PunchType.In, 9, 3), P(PunchType.Out, 17, 0) }, shift);

        Assert.Equal(8.0, result.WorkedHours);
        Assert.True(result.HasDoublePunch);
    }

    [Fact]
    public void FourPunchMode_AllFourPresent_SubtractsBreakDuration()
    {
        var shift = FourPunchShift(new TimeOnly(8, 0));
        var result = AttendanceAnalysisService.Analyze(
            new List<Punch>
            {
                P(PunchType.In, 9), P(PunchType.BreakOut, 13), P(PunchType.BreakIn, 14), P(PunchType.Out, 18),
            }, shift);

        Assert.Equal(7.0, result.WorkedHours); // (13-9) + (18-14) = 4 + 4
    }

    [Fact]
    public void FourPunchMode_MissingBreakIn_WorkedHoursIsNull()
    {
        var shift = FourPunchShift(new TimeOnly(8, 0));
        var result = AttendanceAnalysisService.Analyze(
            new List<Punch> { P(PunchType.In, 9), P(PunchType.BreakOut, 13), P(PunchType.Out, 18) }, shift);

        Assert.Null(result.WorkedHours);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/tests/AttendanceApi.Tests --filter AttendanceAnalysisServiceTests`
Expected: FAIL to compile — `AttendanceAnalysisService` does not exist yet.

- [ ] **Step 3: Write the implementation**

```csharp
// backend/src/AttendanceApi/Services/AttendanceAnalysisService.cs
using AttendanceApi.Entities;

namespace AttendanceApi.Services;

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
    // Stand-in until Tenant grows a real timezone field — every day-boundary and time-of-day
    // comparison in the attendance analytics feature goes through this one constant, so swapping
    // it for a per-tenant lookup later is a one-place change.
    public static readonly TimeSpan DefaultTenantOffset = TimeSpan.FromHours(3);

    private static readonly TimeSpan DoublePunchWindow = TimeSpan.FromMinutes(5);

    public static AttendanceAnalysisResult Analyze(List<Punch> dayPunches, Shift? shift)
    {
        var sorted = dayPunches.OrderBy(p => p.Timestamp).ToList();

        DateTimeOffset? firstIn = sorted.FirstOrDefault(p => p.PunchType == PunchType.In)?.Timestamp;
        DateTimeOffset? lastOut = sorted.LastOrDefault(p => p.PunchType == PunchType.Out)?.Timestamp;

        var workedHours = shift?.PunchMode == PunchMode.FourPunch
            ? WorkedHoursFourPunch(sorted)
            : WorkedHoursTwoPunch(sorted);

        var isMissingCheckout = sorted.Count > 0 && sorted[^1].PunchType != PunchType.Out;
        var hasDoublePunch = HasDoublePunch(sorted);

        var hasShift = shift is not null;
        var isLate = false;
        int? lateMinutes = null;
        if (hasShift && firstIn is not null)
        {
            var localTimeOfDay = firstIn.Value.ToOffset(DefaultTenantOffset).TimeOfDay;
            var threshold = shift!.StartTime.ToTimeSpan() + TimeSpan.FromMinutes(shift.GraceMinutes);
            if (localTimeOfDay > threshold)
            {
                isLate = true;
                lateMinutes = (int)(localTimeOfDay - threshold).TotalMinutes;
            }
        }

        return new AttendanceAnalysisResult(firstIn, lastOut, workedHours, hasShift, isLate, lateMinutes, isMissingCheckout, hasDoublePunch);
    }

    private static double? WorkedHoursTwoPunch(List<Punch> sorted)
    {
        double total = 0;
        var hadCompletedPair = false;
        DateTimeOffset? openIn = null;

        foreach (var punch in sorted)
        {
            if (punch.PunchType == PunchType.In)
            {
                openIn ??= punch.Timestamp;
            }
            else if (punch.PunchType == PunchType.Out && openIn is not null)
            {
                total += (punch.Timestamp - openIn.Value).TotalHours;
                openIn = null;
                hadCompletedPair = true;
            }
        }

        return hadCompletedPair ? total : null;
    }

    private static double? WorkedHoursFourPunch(List<Punch> sorted)
    {
        var firstByType = new Dictionary<PunchType, DateTimeOffset>();
        foreach (var punch in sorted)
        {
            if (!firstByType.ContainsKey(punch.PunchType))
                firstByType[punch.PunchType] = punch.Timestamp;
        }

        if (!firstByType.TryGetValue(PunchType.In, out var inAt) ||
            !firstByType.TryGetValue(PunchType.BreakOut, out var breakOutAt) ||
            !firstByType.TryGetValue(PunchType.BreakIn, out var breakInAt) ||
            !firstByType.TryGetValue(PunchType.Out, out var outAt))
        {
            return null;
        }

        return (breakOutAt - inAt).TotalHours + (outAt - breakInAt).TotalHours;
    }

    private static bool HasDoublePunch(List<Punch> sorted)
    {
        for (var i = 0; i < sorted.Count; i++)
        {
            for (var j = i + 1; j < sorted.Count; j++)
            {
                if (sorted[j].Timestamp - sorted[i].Timestamp > DoublePunchWindow) break;
                if (sorted[i].PunchType == sorted[j].PunchType) return true;
            }
        }
        return false;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/tests/AttendanceApi.Tests --filter AttendanceAnalysisServiceTests`
Expected: PASS (13 tests)

- [ ] **Step 5: Commit**

```bash
git add backend/src/AttendanceApi/Services/AttendanceAnalysisService.cs backend/tests/AttendanceApi.Tests/AttendanceAnalysisServiceTests.cs
git commit -m "feat: add AttendanceAnalysisService (late/missing-checkout/double-punch/worked-hours)"
```

---

### Task 2: Extend the Daily endpoint (absent employees, richer DTO)

**Files:**
- Modify: `backend/src/AttendanceApi/Dtos/AttendanceDtos.cs`
- Modify: `backend/src/AttendanceApi/Controllers/AttendanceController.cs`
- Modify: `backend/tests/AttendanceApi.Tests/AttendanceControllerTests.cs`
- Modify: `portal/src/app/(dashboard)/attendance/page.tsx:3` (type import — `DailyAttendanceResponse` → `AttendanceRowResponse`)
- Modify: `portal/src/lib/types.ts`

**Interfaces:**
- Consumes: `AttendanceAnalysisService.Analyze(List<Punch>, Shift?)` from Task 1.
- Produces: `AttendanceRowResponse(Guid EmployeeId, string EmployeeName, DateOnly Date, Guid? ShiftId, DateTimeOffset? FirstIn, DateTimeOffset? LastOut, double? WorkedHours, bool HasShift, bool IsLate, int? LateMinutes, bool IsMissingCheckout, bool HasDoublePunch)` and `AttendanceController.BuildDailyRowsAsync(Guid tenantId, DateOnly date)` — Tasks 3 and 4 call this exact helper.

- [ ] **Step 1: Update the DTO**

Replace the contents of `backend/src/AttendanceApi/Dtos/AttendanceDtos.cs`:

```csharp
namespace AttendanceApi.Dtos;

public record AttendanceRowResponse(
    Guid EmployeeId,
    string EmployeeName,
    DateOnly Date,
    Guid? ShiftId,
    DateTimeOffset? FirstIn,
    DateTimeOffset? LastOut,
    double? WorkedHours,
    bool HasShift,
    bool IsLate,
    int? LateMinutes,
    bool IsMissingCheckout,
    bool HasDoublePunch);
```

- [ ] **Step 2: Write the failing tests**

Replace `backend/tests/AttendanceApi.Tests/AttendanceControllerTests.cs` in full (renames `DailyAttendanceResponse` → `AttendanceRowResponse` throughout the four existing tests, and adds two new tests):

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AttendanceApi.Data;
using AttendanceApi.Dtos;
using AttendanceApi.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AttendanceApi.Tests;

public class AttendanceControllerTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public AttendanceControllerTests(ApiFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> TenantAdminClientAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = new PasswordHasher<User>();
        var user = new User { Email = $"admin-{Guid.NewGuid()}@zak.test", PasswordHash = "", Role = UserRole.TenantAdmin, TenantId = tenantId };
        user.PasswordHash = hasher.HashPassword(user, "correct-horse");
        db.Users.Add(user);
        db.SaveChanges();

        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, "correct-horse"));
        var body = await login.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Token);
        return client;
    }

    [Fact]
    public async Task DailyView_ReturnsFirstInAndLastOut()
    {
        Guid tenantId, employeeId;
        var day = new DateOnly(2026, 8, 10);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = new Tenant { Name = "Acme Foods", DeviceVendor = DeviceVendor.Zk4500 };
            var employee = new Employee { TenantId = tenant.Id, EmployeeCode = "E001", Name = "Jane Doe" };
            var station = new Station { TenantId = tenant.Id, Name = "Front Desk", DeviceVendor = DeviceVendor.Zk4500, ApiKeyHash = "hash" };
            db.Tenants.Add(tenant);
            db.Employees.Add(employee);
            db.Stations.Add(station);
            db.Punches.AddRange(
                new Punch { Id = Guid.NewGuid(), TenantId = tenant.Id, EmployeeId = employee.Id, StationId = station.Id, PunchType = PunchType.In, Timestamp = new DateTimeOffset(day.ToDateTime(new TimeOnly(9, 2)), TimeSpan.Zero) },
                new Punch { Id = Guid.NewGuid(), TenantId = tenant.Id, EmployeeId = employee.Id, StationId = station.Id, PunchType = PunchType.Out, Timestamp = new DateTimeOffset(day.ToDateTime(new TimeOnly(17, 5)), TimeSpan.Zero) });
            db.SaveChanges();
            tenantId = tenant.Id;
            employeeId = employee.Id;
        }

        var client = await TenantAdminClientAsync(tenantId);
        var response = await client.GetAsync("/api/attendance/daily?date=2026-08-10");
        var rows = await response.Content.ReadFromJsonAsync<List<AttendanceRowResponse>>();

        var row = Assert.Single(rows!, r => r.EmployeeId == employeeId);
        Assert.Equal(9, row.FirstIn!.Value.Hour);
        Assert.Equal(17, row.LastOut!.Value.Hour);
    }

    [Fact]
    public async Task DailyView_DoesNotIncludeOtherTenantsEmployees()
    {
        var day = new DateOnly(2026, 8, 11);

        Guid tenantAId, employeeAId, employeeBId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var tenantA = new Tenant { Name = "Acme Foods A", DeviceVendor = DeviceVendor.Zk4500 };
            var employeeA = new Employee { TenantId = tenantA.Id, EmployeeCode = "A001", Name = "Alice A" };
            var stationA = new Station { TenantId = tenantA.Id, Name = "Front Desk A", DeviceVendor = DeviceVendor.Zk4500, ApiKeyHash = "hashA" };

            var tenantB = new Tenant { Name = "Acme Foods B", DeviceVendor = DeviceVendor.Zk4500 };
            var employeeB = new Employee { TenantId = tenantB.Id, EmployeeCode = "B001", Name = "Bob B" };
            var stationB = new Station { TenantId = tenantB.Id, Name = "Front Desk B", DeviceVendor = DeviceVendor.Zk4500, ApiKeyHash = "hashB" };

            db.Tenants.AddRange(tenantA, tenantB);
            db.Employees.AddRange(employeeA, employeeB);
            db.Stations.AddRange(stationA, stationB);
            db.Punches.AddRange(
                new Punch { Id = Guid.NewGuid(), TenantId = tenantA.Id, EmployeeId = employeeA.Id, StationId = stationA.Id, PunchType = PunchType.In, Timestamp = new DateTimeOffset(day.ToDateTime(new TimeOnly(8, 0)), TimeSpan.Zero) },
                new Punch { Id = Guid.NewGuid(), TenantId = tenantA.Id, EmployeeId = employeeA.Id, StationId = stationA.Id, PunchType = PunchType.Out, Timestamp = new DateTimeOffset(day.ToDateTime(new TimeOnly(16, 0)), TimeSpan.Zero) },
                new Punch { Id = Guid.NewGuid(), TenantId = tenantB.Id, EmployeeId = employeeB.Id, StationId = stationB.Id, PunchType = PunchType.In, Timestamp = new DateTimeOffset(day.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero) },
                new Punch { Id = Guid.NewGuid(), TenantId = tenantB.Id, EmployeeId = employeeB.Id, StationId = stationB.Id, PunchType = PunchType.Out, Timestamp = new DateTimeOffset(day.ToDateTime(new TimeOnly(17, 0)), TimeSpan.Zero) });
            db.SaveChanges();

            tenantAId = tenantA.Id;
            employeeAId = employeeA.Id;
            employeeBId = employeeB.Id;
        }

        var client = await TenantAdminClientAsync(tenantAId);
        var response = await client.GetAsync("/api/attendance/daily?date=2026-08-11");
        var rows = await response.Content.ReadFromJsonAsync<List<AttendanceRowResponse>>();

        Assert.Contains(rows!, r => r.EmployeeId == employeeAId);
        Assert.DoesNotContain(rows!, r => r.EmployeeId == employeeBId);
    }

    [Fact]
    public async Task DailyView_NameLookupIsTenantScoped_EvenForBadDataWithForeignEmployeeId()
    {
        // Simulates data that predates the ingestion-side tenant check: a punch recorded under
        // tenant A's stationId/tenantId but pointing at tenant B's employee row. The endpoint must
        // still surface the row (defensive behavior for bad data) but never resolve the foreign
        // employee's real name.
        var day = new DateOnly(2026, 8, 12);

        Guid tenantAId, foreignEmployeeId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var tenantA = new Tenant { Name = "Acme Foods A2", DeviceVendor = DeviceVendor.Zk4500 };
            var stationA = new Station { TenantId = tenantA.Id, Name = "Front Desk A2", DeviceVendor = DeviceVendor.Zk4500, ApiKeyHash = $"hash-{Guid.NewGuid()}" };

            var tenantB = new Tenant { Name = "Acme Foods B2", DeviceVendor = DeviceVendor.Zk4500 };
            var employeeB = new Employee { TenantId = tenantB.Id, EmployeeCode = "B002", Name = "Secret Bob" };

            db.Tenants.AddRange(tenantA, tenantB);
            db.Stations.Add(stationA);
            db.Employees.Add(employeeB);

            db.Punches.Add(new Punch
            {
                Id = Guid.NewGuid(),
                TenantId = tenantA.Id,
                EmployeeId = employeeB.Id,
                StationId = stationA.Id,
                PunchType = PunchType.In,
                Timestamp = new DateTimeOffset(day.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero),
            });
            db.SaveChanges();

            tenantAId = tenantA.Id;
            foreignEmployeeId = employeeB.Id;
        }

        var client = await TenantAdminClientAsync(tenantAId);
        var response = await client.GetAsync("/api/attendance/daily?date=2026-08-12");
        var rows = await response.Content.ReadFromJsonAsync<List<AttendanceRowResponse>>();

        var row = Assert.Single(rows!, r => r.EmployeeId == foreignEmployeeId);
        Assert.Equal("Unknown", row.EmployeeName);
        Assert.False(row.HasShift);
    }

    [Fact]
    public async Task DailyView_ExcludesPunchesFromOtherDates()
    {
        var dayOne = new DateOnly(2026, 8, 10);
        var dayTwo = new DateOnly(2026, 8, 11);

        Guid tenantId, employeeId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = new Tenant { Name = "Acme Foods", DeviceVendor = DeviceVendor.Zk4500 };
            var employee = new Employee { TenantId = tenant.Id, EmployeeCode = "E001", Name = "Jane Doe" };
            var station = new Station { TenantId = tenant.Id, Name = "Front Desk", DeviceVendor = DeviceVendor.Zk4500, ApiKeyHash = $"hash-{Guid.NewGuid()}" };
            db.Tenants.Add(tenant);
            db.Employees.Add(employee);
            db.Stations.Add(station);
            db.Punches.AddRange(
                new Punch { Id = Guid.NewGuid(), TenantId = tenant.Id, EmployeeId = employee.Id, StationId = station.Id, PunchType = PunchType.In, Timestamp = new DateTimeOffset(dayOne.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero) },
                new Punch { Id = Guid.NewGuid(), TenantId = tenant.Id, EmployeeId = employee.Id, StationId = station.Id, PunchType = PunchType.In, Timestamp = new DateTimeOffset(dayTwo.ToDateTime(new TimeOnly(23, 0)), TimeSpan.Zero) });
            db.SaveChanges();
            tenantId = tenant.Id;
            employeeId = employee.Id;
        }

        var client = await TenantAdminClientAsync(tenantId);
        var response = await client.GetAsync("/api/attendance/daily?date=2026-08-10");
        var rows = await response.Content.ReadFromJsonAsync<List<AttendanceRowResponse>>();

        var row = Assert.Single(rows!, r => r.EmployeeId == employeeId);
        Assert.Equal(9, row.FirstIn!.Value.Hour);
        Assert.Equal(9, row.LastOut!.Value.Hour);
    }

    [Fact]
    public async Task DailyView_EmployeeWithShiftAndNoPunches_AppearsAsAbsent()
    {
        var day = new DateOnly(2026, 8, 13);

        Guid tenantId, employeeId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = new Tenant { Name = "Acme Foods", DeviceVendor = DeviceVendor.Zk4500 };
            var shift = new Shift { TenantId = tenant.Id, Name = "Day", StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(17, 0), GraceMinutes = 5 };
            var employee = new Employee { TenantId = tenant.Id, EmployeeCode = "E010", Name = "No Show", ShiftId = shift.Id };
            db.Tenants.Add(tenant);
            db.Shifts.Add(shift);
            db.Employees.Add(employee);
            db.SaveChanges();
            tenantId = tenant.Id;
            employeeId = employee.Id;
        }

        var client = await TenantAdminClientAsync(tenantId);
        var response = await client.GetAsync($"/api/attendance/daily?date={day:yyyy-MM-dd}");
        var rows = await response.Content.ReadFromJsonAsync<List<AttendanceRowResponse>>();

        var row = Assert.Single(rows!, r => r.EmployeeId == employeeId);
        Assert.True(row.HasShift);
        Assert.Null(row.FirstIn);
        Assert.Null(row.LastOut);
        Assert.False(row.IsLate);
    }

    [Fact]
    public async Task DailyView_LateArrival_IsFlaggedWithMinutes()
    {
        var day = new DateOnly(2026, 8, 14);

        Guid tenantId, employeeId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = new Tenant { Name = "Acme Foods", DeviceVendor = DeviceVendor.Zk4500 };
            var shift = new Shift { TenantId = tenant.Id, Name = "Day", StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(17, 0), GraceMinutes = 5 };
            var employee = new Employee { TenantId = tenant.Id, EmployeeCode = "E011", Name = "Late Larry", ShiftId = shift.Id };
            var station = new Station { TenantId = tenant.Id, Name = "Front Desk", DeviceVendor = DeviceVendor.Zk4500, ApiKeyHash = $"hash-{Guid.NewGuid()}" };
            db.Tenants.Add(tenant);
            db.Shifts.Add(shift);
            db.Employees.Add(employee);
            db.Stations.Add(station);
            // 06:20 UTC = 09:20 local (GMT+3); shift threshold is 08:05 local.
            db.Punches.Add(new Punch { Id = Guid.NewGuid(), TenantId = tenant.Id, EmployeeId = employee.Id, StationId = station.Id, PunchType = PunchType.In, Timestamp = new DateTimeOffset(day.ToDateTime(new TimeOnly(6, 20)), TimeSpan.Zero) });
            db.SaveChanges();
            tenantId = tenant.Id;
            employeeId = employee.Id;
        }

        var client = await TenantAdminClientAsync(tenantId);
        var response = await client.GetAsync($"/api/attendance/daily?date={day:yyyy-MM-dd}");
        var rows = await response.Content.ReadFromJsonAsync<List<AttendanceRowResponse>>();

        var row = Assert.Single(rows!, r => r.EmployeeId == employeeId);
        Assert.True(row.IsLate);
        Assert.Equal(75, row.LateMinutes);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test backend/tests/AttendanceApi.Tests --filter AttendanceControllerTests`
Expected: FAIL to compile — `AttendanceRowResponse` isn't produced by the controller yet, and `BuildDailyRowsAsync` doesn't exist.

- [ ] **Step 4: Rewrite the controller**

Replace the contents of `backend/src/AttendanceApi/Controllers/AttendanceController.cs`:

```csharp
using AttendanceApi.Auth;
using AttendanceApi.Data;
using AttendanceApi.Dtos;
using AttendanceApi.Entities;
using AttendanceApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceApi.Controllers;

[ApiController]
[Route("api/attendance")]
[Authorize(Policy = AuthorizationPolicies.TenantAdmin)]
public class AttendanceController : ControllerBase
{
    private readonly AppDbContext _db;

    public AttendanceController(AppDbContext db) => _db = db;

    [HttpGet("daily")]
    public async Task<ActionResult<List<AttendanceRowResponse>>> Daily([FromQuery] DateOnly date)
    {
        var tenantId = User.TenantId()!.Value;
        return await BuildDailyRowsAsync(tenantId, date);
    }

    private async Task<List<AttendanceRowResponse>> BuildDailyRowsAsync(Guid tenantId, DateOnly date)
    {
        var offset = AttendanceAnalysisService.DefaultTenantOffset;
        var start = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), offset);
        var end = start.AddDays(1);

        var punches = await _db.Punches
            .Where(p => p.TenantId == tenantId && p.Timestamp >= start && p.Timestamp < end)
            .ToListAsync();

        var employees = await _db.Employees.Where(e => e.TenantId == tenantId).ToListAsync();
        var shifts = await _db.Shifts.Where(s => s.TenantId == tenantId).ToDictionaryAsync(s => s.Id);
        var employeesById = employees.ToDictionary(e => e.Id);

        // Union, not a plain join: an employee with a shift but zero punches must still appear
        // (absent), and a punch pointing at an employee id outside this tenant must still appear
        // too (defensive — see DailyView_NameLookupIsTenantScoped_EvenForBadDataWithForeignEmployeeId).
        var employeeIds = employees.Select(e => e.Id).Union(punches.Select(p => p.EmployeeId));

        var rows = new List<AttendanceRowResponse>();
        foreach (var id in employeeIds)
        {
            var employee = employeesById.GetValueOrDefault(id);
            var shift = employee?.ShiftId is Guid shiftId ? shifts.GetValueOrDefault(shiftId) : null;
            var dayPunches = punches.Where(p => p.EmployeeId == id).ToList();
            var result = AttendanceAnalysisService.Analyze(dayPunches, shift);

            rows.Add(new AttendanceRowResponse(
                id,
                employee?.Name ?? "Unknown",
                date,
                employee?.ShiftId,
                result.FirstIn,
                result.LastOut,
                result.WorkedHours,
                result.HasShift,
                result.IsLate,
                result.LateMinutes,
                result.IsMissingCheckout,
                result.HasDoublePunch));
        }

        return rows;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test backend/tests/AttendanceApi.Tests --filter AttendanceControllerTests`
Expected: PASS (6 tests)

- [ ] **Step 6: Update the portal type and its one usage**

In `portal/src/lib/types.ts`, replace the `DailyAttendanceResponse` type:

```ts
export type AttendanceRowResponse = {
  employeeId: string;
  employeeName: string;
  date: string;
  shiftId: string | null;
  firstIn: string | null;
  lastOut: string | null;
  workedHours: number | null;
  hasShift: boolean;
  isLate: boolean;
  lateMinutes: number | null;
  isMissingCheckout: boolean;
  hasDoublePunch: boolean;
};
```

In `portal/src/app/(dashboard)/attendance/page.tsx:3`, change:

```ts
import type { DailyAttendanceResponse } from "@/lib/types";
```

to:

```ts
import type { AttendanceRowResponse } from "@/lib/types";
```

and on line 28, change `DailyAttendanceResponse[]` to `AttendanceRowResponse[]`. (Task 6 replaces this whole file's body with the enriched table — this step only keeps it compiling.)

- [ ] **Step 7: Run the portal test suite to confirm nothing broke**

Run: `cd portal && npm test`
Expected: PASS (existing suite, unchanged behavior)

- [ ] **Step 8: Commit**

```bash
git add backend/src/AttendanceApi/Dtos/AttendanceDtos.cs backend/src/AttendanceApi/Controllers/AttendanceController.cs backend/tests/AttendanceApi.Tests/AttendanceControllerTests.cs portal/src/lib/types.ts "portal/src/app/(dashboard)/attendance/page.tsx"
git commit -m "feat: extend daily attendance endpoint with absent detection, late/hours/anomaly fields"
```

---

### Task 3: Summary endpoint (dashboard counts)

**Files:**
- Modify: `backend/src/AttendanceApi/Dtos/AttendanceDtos.cs`
- Modify: `backend/src/AttendanceApi/Controllers/AttendanceController.cs`
- Modify: `backend/tests/AttendanceApi.Tests/AttendanceControllerTests.cs`

**Interfaces:**
- Consumes: `BuildDailyRowsAsync(Guid, DateOnly)` from Task 2.
- Produces: `AttendanceSummaryResponse(int TotalEmployeesWithShift, int PresentCount, int AbsentCount, int LateCount)` — Task 5 (portal dashboard) consumes this exact shape.

- [ ] **Step 1: Add the DTO**

Append to `backend/src/AttendanceApi/Dtos/AttendanceDtos.cs`:

```csharp

public record AttendanceSummaryResponse(int TotalEmployeesWithShift, int PresentCount, int AbsentCount, int LateCount);
```

- [ ] **Step 2: Write the failing test**

Append to `backend/tests/AttendanceApi.Tests/AttendanceControllerTests.cs` (inside the class, before the final closing brace):

```csharp

    [Fact]
    public async Task Summary_CountsPresentAbsentLate_ExcludingEmployeesWithoutAShift()
    {
        var day = new DateOnly(2026, 8, 15);

        Guid tenantId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = new Tenant { Name = "Acme Foods", DeviceVendor = DeviceVendor.Zk4500 };
            var shift = new Shift { TenantId = tenant.Id, Name = "Day", StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(17, 0), GraceMinutes = 5 };
            var station = new Station { TenantId = tenant.Id, Name = "Front Desk", DeviceVendor = DeviceVendor.Zk4500, ApiKeyHash = $"hash-{Guid.NewGuid()}" };

            var present = new Employee { TenantId = tenant.Id, EmployeeCode = "E020", Name = "Present Pat", ShiftId = shift.Id };
            var absent = new Employee { TenantId = tenant.Id, EmployeeCode = "E021", Name = "Absent Al", ShiftId = shift.Id };
            var late = new Employee { TenantId = tenant.Id, EmployeeCode = "E022", Name = "Late Larry", ShiftId = shift.Id };
            var noShift = new Employee { TenantId = tenant.Id, EmployeeCode = "E023", Name = "No Shift Nia" };

            db.Tenants.Add(tenant);
            db.Shifts.Add(shift);
            db.Stations.Add(station);
            db.Employees.AddRange(present, absent, late, noShift);
            db.Punches.AddRange(
                new Punch { Id = Guid.NewGuid(), TenantId = tenant.Id, EmployeeId = present.Id, StationId = station.Id, PunchType = PunchType.In, Timestamp = new DateTimeOffset(day.ToDateTime(new TimeOnly(5, 0)), TimeSpan.Zero) }, // 08:00 local, on time
                new Punch { Id = Guid.NewGuid(), TenantId = tenant.Id, EmployeeId = late.Id, StationId = station.Id, PunchType = PunchType.In, Timestamp = new DateTimeOffset(day.ToDateTime(new TimeOnly(7, 0)), TimeSpan.Zero) }); // 10:00 local, late
            db.SaveChanges();
            tenantId = tenant.Id;
        }

        var client = await TenantAdminClientAsync(tenantId);
        var response = await client.GetAsync($"/api/attendance/summary?date={day:yyyy-MM-dd}");
        var summary = await response.Content.ReadFromJsonAsync<AttendanceSummaryResponse>();

        Assert.Equal(3, summary!.TotalEmployeesWithShift); // noShift excluded
        Assert.Equal(2, summary.PresentCount); // present + late both punched in
        Assert.Equal(1, summary.AbsentCount); // absent
        Assert.Equal(1, summary.LateCount); // late
    }
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test backend/tests/AttendanceApi.Tests --filter Summary_CountsPresentAbsentLate`
Expected: FAIL — `/api/attendance/summary` returns 404.

- [ ] **Step 4: Add the Summary action**

Add to `backend/src/AttendanceApi/Controllers/AttendanceController.cs`, directly after the `Daily` action:

```csharp

    [HttpGet("summary")]
    public async Task<ActionResult<AttendanceSummaryResponse>> Summary([FromQuery] DateOnly date)
    {
        var tenantId = User.TenantId()!.Value;
        var rows = await BuildDailyRowsAsync(tenantId, date);
        var withShift = rows.Where(r => r.HasShift).ToList();

        var present = withShift.Count(r => r.FirstIn is not null);
        var absent = withShift.Count(r => r.FirstIn is null);
        var late = withShift.Count(r => r.IsLate);

        return new AttendanceSummaryResponse(withShift.Count, present, absent, late);
    }
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test backend/tests/AttendanceApi.Tests --filter Summary_CountsPresentAbsentLate`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add backend/src/AttendanceApi/Dtos/AttendanceDtos.cs backend/src/AttendanceApi/Controllers/AttendanceController.cs backend/tests/AttendanceApi.Tests/AttendanceControllerTests.cs
git commit -m "feat: add attendance summary endpoint for the tenant dashboard"
```

---

### Task 4: Report and Report/Export endpoints

**Files:**
- Modify: `backend/src/AttendanceApi/Controllers/AttendanceController.cs`
- Modify: `backend/tests/AttendanceApi.Tests/AttendanceControllerTests.cs`

**Interfaces:**
- Consumes: `BuildDailyRowsAsync(Guid, DateOnly)` from Task 2.
- Produces: `GET /api/attendance/report?from=&to=` (List<AttendanceRowResponse>), `GET /api/attendance/report/export?from=&to=` (CSV file) — Task 7 (portal reports page) consumes both.

- [ ] **Step 1: Write the failing tests**

Append to `backend/tests/AttendanceApi.Tests/AttendanceControllerTests.cs` (inside the class, before the final closing brace):

```csharp

    [Fact]
    public async Task Report_ReturnsOneRowPerEmployeePerDayInRange()
    {
        Guid tenantId, employeeId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = new Tenant { Name = "Acme Foods", DeviceVendor = DeviceVendor.Zk4500 };
            var shift = new Shift { TenantId = tenant.Id, Name = "Day", StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(17, 0), GraceMinutes = 5 };
            var employee = new Employee { TenantId = tenant.Id, EmployeeCode = "E030", Name = "Range Rita", ShiftId = shift.Id };
            db.Tenants.Add(tenant);
            db.Shifts.Add(shift);
            db.Employees.Add(employee);
            db.SaveChanges();
            tenantId = tenant.Id;
            employeeId = employee.Id;
        }

        var client = await TenantAdminClientAsync(tenantId);
        var response = await client.GetAsync("/api/attendance/report?from=2026-08-20&to=2026-08-22");
        var rows = await response.Content.ReadFromJsonAsync<List<AttendanceRowResponse>>();

        // Has a shift, so every day in the 3-day range gets a row even with zero punches.
        Assert.Equal(3, rows!.Count(r => r.EmployeeId == employeeId));
    }

    [Fact]
    public async Task Report_ToBeforeFrom_ReturnsBadRequest()
    {
        var tenantId = await CreateBareTenantAsync();
        var client = await TenantAdminClientAsync(tenantId);

        var response = await client.GetAsync("/api/attendance/report?from=2026-08-22&to=2026-08-20");

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Report_RangeOverNinetyDays_ReturnsBadRequest()
    {
        var tenantId = await CreateBareTenantAsync();
        var client = await TenantAdminClientAsync(tenantId);

        var response = await client.GetAsync("/api/attendance/report?from=2026-01-01&to=2026-04-15"); // 105 days

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReportExport_ReturnsCsvWithHeaderRow()
    {
        Guid tenantId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = new Tenant { Name = "Acme Foods", DeviceVendor = DeviceVendor.Zk4500 };
            var shift = new Shift { TenantId = tenant.Id, Name = "Day", StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(17, 0), GraceMinutes = 5 };
            var employee = new Employee { TenantId = tenant.Id, EmployeeCode = "E031", Name = "Export Eve", ShiftId = shift.Id };
            db.Tenants.Add(tenant);
            db.Shifts.Add(shift);
            db.Employees.Add(employee);
            db.SaveChanges();
            tenantId = tenant.Id;
        }

        var client = await TenantAdminClientAsync(tenantId);
        var response = await client.GetAsync("/api/attendance/report/export?from=2026-08-20&to=2026-08-20");

        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        var csv = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("Employee,Date,First In,Last Out,Worked Hours,Late (minutes),Missing Checkout,Double Punch", csv);
        Assert.Contains("Export Eve", csv);
    }

    private async Task<Guid> CreateBareTenantAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = new Tenant { Name = $"Tenant {Guid.NewGuid()}", DeviceVendor = DeviceVendor.Zk4500 };
        db.Tenants.Add(tenant);
        db.SaveChanges();
        return tenant.Id;
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/tests/AttendanceApi.Tests --filter "Report_|ReportExport_"`
Expected: FAIL — the two endpoints return 404.

- [ ] **Step 3: Add the Report and ReportExport actions**

Add to `backend/src/AttendanceApi/Controllers/AttendanceController.cs`, directly after the `Summary` action (and add `using System.Text;` to the top of the file alongside the existing usings):

```csharp

    [HttpGet("report")]
    public async Task<ActionResult<List<AttendanceRowResponse>>> Report([FromQuery] DateOnly from, [FromQuery] DateOnly to)
    {
        var validation = ValidateRange(from, to);
        if (validation is not null) return validation;

        var tenantId = User.TenantId()!.Value;
        return await BuildReportRowsAsync(tenantId, from, to);
    }

    [HttpGet("report/export")]
    public async Task<IActionResult> ReportExport([FromQuery] DateOnly from, [FromQuery] DateOnly to)
    {
        var validation = ValidateRange(from, to);
        if (validation is not null) return validation;

        var tenantId = User.TenantId()!.Value;
        var rows = await BuildReportRowsAsync(tenantId, from, to);
        var bytes = Encoding.UTF8.GetBytes(ToCsv(rows));
        return File(bytes, "text/csv", $"attendance-{from:yyyy-MM-dd}-to-{to:yyyy-MM-dd}.csv");
    }

    private ActionResult? ValidateRange(DateOnly from, DateOnly to)
    {
        if (to < from) return BadRequest("'to' must not be before 'from'.");
        var daysInclusive = to.DayNumber - from.DayNumber + 1;
        if (daysInclusive > 90) return BadRequest("Date range cannot exceed 90 days.");
        return null;
    }

    private async Task<List<AttendanceRowResponse>> BuildReportRowsAsync(Guid tenantId, DateOnly from, DateOnly to)
    {
        var rows = new List<AttendanceRowResponse>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var dayRows = await BuildDailyRowsAsync(tenantId, date);
            rows.AddRange(dayRows.Where(r => r.HasShift || r.FirstIn is not null || r.LastOut is not null));
        }
        return rows;
    }

    private static string ToCsv(List<AttendanceRowResponse> rows)
    {
        var sb = new StringBuilder();
        sb.Append("Employee,Date,First In,Last Out,Worked Hours,Late (minutes),Missing Checkout,Double Punch\n");

        var offset = AttendanceAnalysisService.DefaultTenantOffset;
        foreach (var r in rows)
        {
            var fields = new[]
            {
                CsvEscape(r.EmployeeName),
                r.Date.ToString("yyyy-MM-dd"),
                r.FirstIn?.ToOffset(offset).ToString("HH:mm") ?? "",
                r.LastOut?.ToOffset(offset).ToString("HH:mm") ?? "",
                r.WorkedHours?.ToString("0.00") ?? "",
                r.IsLate ? r.LateMinutes.ToString() ?? "" : "",
                r.IsMissingCheckout ? "yes" : "",
                r.HasDoublePunch ? "yes" : "",
            };
            sb.Append(string.Join(",", fields));
            sb.Append('\n');
        }

        return sb.ToString();
    }

    private static string CsvEscape(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/tests/AttendanceApi.Tests --filter "Report_|ReportExport_"`
Expected: PASS (4 tests)

- [ ] **Step 5: Run the full backend test suite**

Run: `dotnet test backend/tests/AttendanceApi.Tests`
Expected: PASS (all tests, no regressions)

- [ ] **Step 6: Commit**

```bash
git add backend/src/AttendanceApi/Controllers/AttendanceController.cs backend/tests/AttendanceApi.Tests/AttendanceControllerTests.cs
git commit -m "feat: add date-range attendance report and CSV export endpoints"
```

---

### Task 5: Portal — types, tenant dashboard, nav, landing redirect

**Files:**
- Modify: `portal/src/lib/types.ts`
- Create: `portal/src/app/(dashboard)/dashboard/page.tsx`
- Create: `portal/src/app/(dashboard)/dashboard/page.test.tsx`
- Modify: `portal/src/app/(dashboard)/NavLinks.tsx`
- Modify: `portal/src/app/page.tsx`

**Interfaces:**
- Consumes: `GET /api/attendance/summary?date=` (Task 3) via `backendFetch`.
- Produces: nothing consumed by later tasks.

- [ ] **Step 1: Add the summary type**

Append to `portal/src/lib/types.ts`:

```ts

export type AttendanceSummaryResponse = {
  totalEmployeesWithShift: number;
  presentCount: number;
  absentCount: number;
  lateCount: number;
};
```

- [ ] **Step 2: Write the failing test**

```tsx
// portal/src/app/(dashboard)/dashboard/page.test.tsx
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";

vi.mock("@/lib/backendFetch", () => ({ backendFetch: vi.fn() }));
vi.mock("@/lib/session", () => ({ getToken: vi.fn().mockResolvedValue("jwt-abc") }));

import { backendFetch } from "@/lib/backendFetch";
import DashboardPage from "./page";

describe("DashboardPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(backendFetch).mockResolvedValue({
      totalEmployeesWithShift: 10,
      presentCount: 7,
      absentCount: 2,
      lateCount: 1,
    });
  });

  it("fetches today's summary (GMT+3) and renders the four KPI cards", async () => {
    render(await DashboardPage());

    expect(backendFetch).toHaveBeenCalledWith(
      expect.stringMatching(/^\/api\/attendance\/summary\?date=\d{4}-\d{2}-\d{2}$/),
      expect.anything(),
    );
    expect(screen.getByText("7")).toBeInTheDocument();
    expect(screen.getByText("2")).toBeInTheDocument();
    expect(screen.getByText("1")).toBeInTheDocument();
    expect(screen.getByText("10")).toBeInTheDocument();
    expect(screen.getByText("Present")).toBeInTheDocument();
    expect(screen.getByText("Absent")).toBeInTheDocument();
    expect(screen.getByText("Late")).toBeInTheDocument();
  });
});
```

- [ ] **Step 3: Run test to verify it fails**

Run: `cd portal && npm test -- dashboard/page.test.tsx`
Expected: FAIL — `./page` (dashboard/page.tsx) doesn't exist yet.

- [ ] **Step 4: Write the dashboard page**

```tsx
// portal/src/app/(dashboard)/dashboard/page.tsx
import { backendFetch } from "@/lib/backendFetch";
import { getToken } from "@/lib/session";
import type { AttendanceSummaryResponse } from "@/lib/types";

const GMT_PLUS_3_MS = 3 * 60 * 60 * 1000;

function todayIsoDateGmtPlus3(): string {
  return new Date(Date.now() + GMT_PLUS_3_MS).toISOString().slice(0, 10);
}

export default async function DashboardPage() {
  const date = todayIsoDateGmtPlus3();
  const summary: AttendanceSummaryResponse = await backendFetch(`/api/attendance/summary?date=${date}`, {
    token: await getToken(),
  });

  const cards = [
    { label: "Present", value: summary.presentCount },
    { label: "Absent", value: summary.absentCount },
    { label: "Late", value: summary.lateCount },
    { label: "Total (with shift)", value: summary.totalEmployeesWithShift },
  ];

  return (
    <div className="p-6 space-y-6">
      <h1 className="font-display text-2xl text-ink">Dashboard</h1>
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
        {cards.map((card) => (
          <div key={card.label} className="bg-surface border border-border rounded-lg p-5">
            <div className="text-sm text-ink-soft">{card.label}</div>
            <div className="font-display text-3xl text-ink mt-1">{card.value}</div>
          </div>
        ))}
      </div>
    </div>
  );
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `cd portal && npm test -- dashboard/page.test.tsx`
Expected: PASS

- [ ] **Step 6: Add the nav link and change the landing redirect**

In `portal/src/app/(dashboard)/NavLinks.tsx`, change the `NAV_LINKS` array to:

```ts
const NAV_LINKS = [
  { href: "/dashboard", label: "Dashboard" },
  { href: "/attendance", label: "Attendance" },
  { href: "/employees", label: "Employees" },
  { href: "/shifts", label: "Shifts" },
];
```

Replace the contents of `portal/src/app/page.tsx`:

```tsx
import { redirect } from "next/navigation";

export default function Home() {
  redirect("/dashboard");
}
```

- [ ] **Step 7: Run the full portal test suite**

Run: `cd portal && npm test`
Expected: PASS (no regressions)

- [ ] **Step 8: Commit**

```bash
git add portal/src/lib/types.ts "portal/src/app/(dashboard)/dashboard" "portal/src/app/(dashboard)/NavLinks.tsx" portal/src/app/page.tsx
git commit -m "feat: add tenant dashboard with today's present/absent/late KPIs"
```

---

### Task 6: Portal — enrich the daily attendance page

**Files:**
- Modify: `portal/src/app/(dashboard)/attendance/page.tsx`
- Modify: `portal/src/app/(dashboard)/attendance/page.test.tsx`

**Interfaces:**
- Consumes: `AttendanceRowResponse` (Task 2's portal type) via `backendFetch("/api/attendance/daily?date=...")`.

- [ ] **Step 1: Write the failing test**

Append to `portal/src/app/(dashboard)/attendance/page.test.tsx` (add `render, screen` to the existing `@testing-library/react` import at the top, then add this test inside the `describe` block):

```tsx
  it("renders worked hours and only the badges that apply", async () => {
    vi.mocked(backendFetch).mockResolvedValue([
      {
        employeeId: "e1",
        employeeName: "On Time Otto",
        date: "2026-01-15",
        shiftId: "s1",
        firstIn: "2026-01-15T05:00:00Z",
        lastOut: "2026-01-15T13:00:00Z",
        workedHours: 8,
        hasShift: true,
        isLate: false,
        lateMinutes: null,
        isMissingCheckout: false,
        hasDoublePunch: false,
      },
      {
        employeeId: "e2",
        employeeName: "Late Larry",
        date: "2026-01-15",
        shiftId: "s1",
        firstIn: "2026-01-15T07:00:00Z",
        lastOut: null,
        workedHours: null,
        hasShift: true,
        isLate: true,
        lateMinutes: 75,
        isMissingCheckout: true,
        hasDoublePunch: false,
      },
    ]);

    render(await AttendancePage({ searchParams: Promise.resolve({ date: "2026-01-15" }) }));

    expect(screen.getByText("8.00")).toBeInTheDocument();
    expect(screen.getByText(/late/i)).toBeInTheDocument();
    expect(screen.getByText(/missing checkout/i)).toBeInTheDocument();
    expect(screen.queryByText(/double punch/i)).not.toBeInTheDocument();
  });
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd portal && npm test -- attendance/page.test.tsx`
Expected: FAIL — current page has no worked-hours column or badges.

- [ ] **Step 3: Rewrite the page**

Replace the contents of `portal/src/app/(dashboard)/attendance/page.tsx`:

```tsx
import { backendFetch } from "@/lib/backendFetch";
import { getToken } from "@/lib/session";
import type { AttendanceRowResponse, ShiftResponse } from "@/lib/types";
import { DateNav } from "./DateNav";

const ISO_DATE_PATTERN = /^\d{4}-\d{2}-\d{2}$/;

function todayIsoDate(): string {
  return new Date().toISOString().slice(0, 10);
}

function isValidIsoDate(candidate: string): boolean {
  const asDate = new Date(`${candidate}T00:00:00Z`);
  if (Number.isNaN(asDate.getTime())) return false;
  return asDate.toISOString().slice(0, 10) === candidate;
}

function Badge({ children }: { children: React.ReactNode }) {
  return (
    <span className="inline-block bg-danger-bg text-danger border border-danger/20 rounded px-2 py-0.5 text-xs mr-1">
      {children}
    </span>
  );
}

export default async function AttendancePage({
  searchParams,
}: {
  searchParams: Promise<{ date?: string }>;
}) {
  const { date: requestedDate } = await searchParams;
  const date =
    requestedDate && ISO_DATE_PATTERN.test(requestedDate) && isValidIsoDate(requestedDate)
      ? requestedDate
      : todayIsoDate();
  const token = await getToken();
  const [rows, shifts]: [AttendanceRowResponse[], ShiftResponse[]] = await Promise.all([
    backendFetch(`/api/attendance/daily?date=${date}`, { token }),
    backendFetch("/api/shifts", { token }),
  ]);

  return (
    <div className="p-6 space-y-6">
      <h1 className="font-display text-2xl text-ink">Daily Attendance</h1>
      <DateNav date={date} />
      <div className="overflow-x-auto bg-surface border border-border rounded-lg">
        <table className="w-full text-sm border-collapse">
          <thead>
            <tr className="text-left bg-surface-muted text-ink">
              <th className="py-3 px-4 font-medium">Employee</th>
              <th className="py-3 px-4 font-medium">Shift</th>
              <th className="py-3 px-4 font-medium">First In</th>
              <th className="py-3 px-4 font-medium">Last Out</th>
              <th className="py-3 px-4 font-medium">Worked Hours</th>
              <th className="py-3 px-4 font-medium">Flags</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.employeeId} className="border-b border-border last:border-b-0 hover:bg-surface-muted">
                <td className="py-3 px-4">{row.employeeName}</td>
                <td className="py-3 px-4">{shifts.find((s) => s.id === row.shiftId)?.name ?? "—"}</td>
                <td className="py-3 px-4">{row.firstIn ? new Date(row.firstIn).toLocaleTimeString() : "—"}</td>
                <td className="py-3 px-4">{row.lastOut ? new Date(row.lastOut).toLocaleTimeString() : "—"}</td>
                <td className="py-3 px-4">{row.workedHours !== null ? row.workedHours.toFixed(2) : "—"}</td>
                <td className="py-3 px-4">
                  {row.isLate && <Badge>Late{row.lateMinutes !== null ? ` (${row.lateMinutes}m)` : ""}</Badge>}
                  {row.isMissingCheckout && <Badge>Missing Checkout</Badge>}
                  {row.hasDoublePunch && <Badge>Double Punch</Badge>}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd portal && npm test -- attendance/page.test.tsx`
Expected: PASS (all tests in this file)

- [ ] **Step 5: Commit**

```bash
git add "portal/src/app/(dashboard)/attendance/page.tsx" "portal/src/app/(dashboard)/attendance/page.test.tsx"
git commit -m "feat: show shift, worked hours, and anomaly badges on the daily attendance page"
```

---

### Task 7: Portal — date-range reports page and CSV export

**Files:**
- Create: `portal/src/app/(dashboard)/reports/page.tsx`
- Create: `portal/src/app/(dashboard)/reports/DateRangeNav.tsx`
- Create: `portal/src/app/(dashboard)/reports/export/route.ts`
- Create: `portal/src/app/(dashboard)/reports/page.test.tsx`
- Modify: `portal/src/app/(dashboard)/NavLinks.tsx`

**Interfaces:**
- Consumes: `GET /api/attendance/report?from=&to=` and `GET /api/attendance/report/export?from=&to=` (Task 4), `AttendanceRowResponse`/`ShiftResponse` types, `getToken()` from `@/lib/session`.

- [ ] **Step 1: Write the failing test**

```tsx
// portal/src/app/(dashboard)/reports/page.test.tsx
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";

vi.mock("@/lib/backendFetch", () => ({ backendFetch: vi.fn() }));
vi.mock("@/lib/session", () => ({ getToken: vi.fn().mockResolvedValue("jwt-abc") }));

import { backendFetch } from "@/lib/backendFetch";
import ReportsPage from "./page";

describe("ReportsPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(backendFetch).mockImplementation((path: string) =>
      Promise.resolve(path.startsWith("/api/shifts") ? [] : []),
    );
  });

  it("defaults to the last 7 days when no range is given", async () => {
    await ReportsPage({ searchParams: Promise.resolve({}) });

    expect(backendFetch).toHaveBeenCalledWith(
      expect.stringMatching(/^\/api\/attendance\/report\?from=\d{4}-\d{2}-\d{2}&to=\d{4}-\d{2}-\d{2}$/),
      expect.anything(),
    );
  });

  it("uses the requested from/to when both are valid", async () => {
    await ReportsPage({ searchParams: Promise.resolve({ from: "2026-01-01", to: "2026-01-07" }) });

    expect(backendFetch).toHaveBeenCalledWith(
      "/api/attendance/report?from=2026-01-01&to=2026-01-07",
      expect.anything(),
    );
  });

  it("renders an export link pointing at the export route with the same range", async () => {
    render(await ReportsPage({ searchParams: Promise.resolve({ from: "2026-01-01", to: "2026-01-07" }) }));

    const link = screen.getByRole("link", { name: /export csv/i });
    expect(link).toHaveAttribute("href", "/reports/export?from=2026-01-01&to=2026-01-07");
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd portal && npm test -- reports/page.test.tsx`
Expected: FAIL — `./page` (reports/page.tsx) doesn't exist yet.

- [ ] **Step 3: Write the date-range nav client component**

```tsx
// portal/src/app/(dashboard)/reports/DateRangeNav.tsx
"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";

export function DateRangeNav({ from, to }: { from: string; to: string }) {
  const router = useRouter();
  const [pendingFrom, setPendingFrom] = useState(from);
  const [pendingTo, setPendingTo] = useState(to);

  return (
    <div className="flex flex-wrap items-end gap-3">
      <label className="flex flex-col text-sm text-ink-soft">
        From
        <input
          type="date"
          value={pendingFrom}
          onChange={(e) => setPendingFrom(e.target.value)}
          className="border border-border rounded-md px-3 py-2 text-ink focus:outline-none focus:ring-2 focus:ring-accent focus:border-accent"
        />
      </label>
      <label className="flex flex-col text-sm text-ink-soft">
        To
        <input
          type="date"
          value={pendingTo}
          onChange={(e) => setPendingTo(e.target.value)}
          className="border border-border rounded-md px-3 py-2 text-ink focus:outline-none focus:ring-2 focus:ring-accent focus:border-accent"
        />
      </label>
      <button
        type="button"
        onClick={() => router.push(`/reports?from=${pendingFrom}&to=${pendingTo}`)}
        className="border border-border rounded-md px-4 py-2 text-ink hover:border-accent"
      >
        Apply
      </button>
    </div>
  );
}
```

- [ ] **Step 4: Write the export route handler**

```ts
// portal/src/app/(dashboard)/reports/export/route.ts
import { NextRequest } from "next/server";
import { getToken } from "@/lib/session";

const BACKEND_URL = process.env.BACKEND_API_URL ?? "http://localhost:8080";

export async function GET(request: NextRequest) {
  const from = request.nextUrl.searchParams.get("from") ?? "";
  const to = request.nextUrl.searchParams.get("to") ?? "";
  const token = await getToken();

  const backendResponse = await fetch(
    `${BACKEND_URL}/api/attendance/report/export?from=${from}&to=${to}`,
    { headers: token ? { Authorization: `Bearer ${token}` } : {}, cache: "no-store" },
  );

  if (!backendResponse.ok) {
    return new Response(await backendResponse.text(), { status: backendResponse.status });
  }

  return new Response(backendResponse.body, {
    headers: {
      "Content-Type": "text/csv",
      "Content-Disposition": backendResponse.headers.get("Content-Disposition") ?? "attachment",
    },
  });
}
```

- [ ] **Step 5: Write the reports page**

```tsx
// portal/src/app/(dashboard)/reports/page.tsx
import { backendFetch } from "@/lib/backendFetch";
import { getToken } from "@/lib/session";
import type { AttendanceRowResponse, ShiftResponse } from "@/lib/types";
import { DateRangeNav } from "./DateRangeNav";

const ISO_DATE_PATTERN = /^\d{4}-\d{2}-\d{2}$/;
const GMT_PLUS_3_MS = 3 * 60 * 60 * 1000;

function todayIsoDateGmtPlus3(): string {
  return new Date(Date.now() + GMT_PLUS_3_MS).toISOString().slice(0, 10);
}

function isValidIsoDate(candidate: string): boolean {
  const asDate = new Date(`${candidate}T00:00:00Z`);
  if (Number.isNaN(asDate.getTime())) return false;
  return asDate.toISOString().slice(0, 10) === candidate;
}

function defaultRange(): { from: string; to: string } {
  const to = todayIsoDateGmtPlus3();
  const from = new Date(new Date(`${to}T00:00:00Z`).getTime() - 6 * 24 * 60 * 60 * 1000)
    .toISOString()
    .slice(0, 10);
  return { from, to };
}

export default async function ReportsPage({
  searchParams,
}: {
  searchParams: Promise<{ from?: string; to?: string }>;
}) {
  const { from: requestedFrom, to: requestedTo } = await searchParams;
  const fallback = defaultRange();
  const from =
    requestedFrom && ISO_DATE_PATTERN.test(requestedFrom) && isValidIsoDate(requestedFrom)
      ? requestedFrom
      : fallback.from;
  const to =
    requestedTo && ISO_DATE_PATTERN.test(requestedTo) && isValidIsoDate(requestedTo)
      ? requestedTo
      : fallback.to;

  const token = await getToken();
  const [rows, shifts]: [AttendanceRowResponse[], ShiftResponse[]] = await Promise.all([
    backendFetch(`/api/attendance/report?from=${from}&to=${to}`, { token }),
    backendFetch("/api/shifts", { token }),
  ]);

  return (
    <div className="p-6 space-y-6">
      <h1 className="font-display text-2xl text-ink">Reports</h1>
      <div className="flex flex-wrap items-end justify-between gap-4">
        <DateRangeNav from={from} to={to} />
        <a
          href={`/reports/export?from=${from}&to=${to}`}
          className="border border-border rounded-md px-4 py-2 text-ink hover:border-accent"
        >
          Export CSV
        </a>
      </div>
      <div className="overflow-x-auto bg-surface border border-border rounded-lg">
        <table className="w-full text-sm border-collapse">
          <thead>
            <tr className="text-left bg-surface-muted text-ink">
              <th className="py-3 px-4 font-medium">Employee</th>
              <th className="py-3 px-4 font-medium">Date</th>
              <th className="py-3 px-4 font-medium">Shift</th>
              <th className="py-3 px-4 font-medium">First In</th>
              <th className="py-3 px-4 font-medium">Last Out</th>
              <th className="py-3 px-4 font-medium">Worked Hours</th>
              <th className="py-3 px-4 font-medium">Flags</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={`${row.employeeId}-${row.date}`} className="border-b border-border last:border-b-0 hover:bg-surface-muted">
                <td className="py-3 px-4">{row.employeeName}</td>
                <td className="py-3 px-4">{row.date}</td>
                <td className="py-3 px-4">{shifts.find((s) => s.id === row.shiftId)?.name ?? "—"}</td>
                <td className="py-3 px-4">{row.firstIn ? new Date(row.firstIn).toLocaleTimeString() : "—"}</td>
                <td className="py-3 px-4">{row.lastOut ? new Date(row.lastOut).toLocaleTimeString() : "—"}</td>
                <td className="py-3 px-4">{row.workedHours !== null ? row.workedHours.toFixed(2) : "—"}</td>
                <td className="py-3 px-4">
                  {row.isLate && <span className="text-danger text-xs mr-1">Late</span>}
                  {row.isMissingCheckout && <span className="text-danger text-xs mr-1">Missing Checkout</span>}
                  {row.hasDoublePunch && <span className="text-danger text-xs mr-1">Double Punch</span>}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
```

- [ ] **Step 6: Run test to verify it passes**

Run: `cd portal && npm test -- reports/page.test.tsx`
Expected: PASS (3 tests)

- [ ] **Step 7: Add the nav link**

In `portal/src/app/(dashboard)/NavLinks.tsx`, add a Reports entry to `NAV_LINKS` (after `Shifts`):

```ts
const NAV_LINKS = [
  { href: "/dashboard", label: "Dashboard" },
  { href: "/attendance", label: "Attendance" },
  { href: "/employees", label: "Employees" },
  { href: "/shifts", label: "Shifts" },
  { href: "/reports", label: "Reports" },
];
```

- [ ] **Step 8: Run the full portal test suite, then the backend suite once more**

Run: `cd portal && npm test`
Expected: PASS (all tests)

Run: `cd backend && dotnet test`
Expected: PASS (all tests)

- [ ] **Step 9: Manually verify in the browser**

Run: `cd portal && npm run build && npm start` (with the backend running on the port configured in `portal/.env.local`)
- Log in as a TenantAdmin, confirm the "Dashboard" nav item loads and shows four KPI numbers.
- Visit "Attendance", confirm the Shift/Worked Hours/Flags columns render and an absent employee (if any test data has one) shows blank First In/Last Out with no crash.
- Visit "Reports", change the date range, confirm the table updates, then click "Export CSV" and confirm a `.csv` file downloads with the expected columns.

- [ ] **Step 10: Commit**

```bash
git add "portal/src/app/(dashboard)/reports" "portal/src/app/(dashboard)/NavLinks.tsx"
git commit -m "feat: add date-range attendance reports page with CSV export"
```
