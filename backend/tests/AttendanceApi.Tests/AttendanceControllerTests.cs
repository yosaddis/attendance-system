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
        // The single in-window punch is PunchType.In; AttendanceAnalysisService.Analyze (Task 1)
        // derives LastOut only from PunchType.Out punches, so it is correctly null here rather
        // than aliasing to the same timestamp as FirstIn (the pre-Task-1 naive Max(timestamp)
        // behavior this test originally asserted).
        Assert.Null(row.LastOut);
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
}
