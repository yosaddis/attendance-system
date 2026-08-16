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
                new Punch { Id = Guid.NewGuid(), TenantId = tenant.Id, EmployeeId = employee.Id, StationId = station.Id, PunchType = PunchType.In, Timestamp = day.ToDateTime(new TimeOnly(9, 2)) },
                new Punch { Id = Guid.NewGuid(), TenantId = tenant.Id, EmployeeId = employee.Id, StationId = station.Id, PunchType = PunchType.Out, Timestamp = day.ToDateTime(new TimeOnly(17, 5)) });
            db.SaveChanges();
            tenantId = tenant.Id;
            employeeId = employee.Id;
        }

        var client = _factory.CreateClient();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hasher = new PasswordHasher<User>();
            var user = new User { Email = $"admin-{Guid.NewGuid()}@zak.test", PasswordHash = "", Role = UserRole.TenantAdmin, TenantId = tenantId };
            user.PasswordHash = hasher.HashPassword(user, "correct-horse");
            db.Users.Add(user);
            db.SaveChanges();

            var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, "correct-horse"));
            var body = await login.Content.ReadFromJsonAsync<LoginResponse>();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Token);
        }

        var response = await client.GetAsync("/api/attendance/daily?date=2026-08-10");
        var rows = await response.Content.ReadFromJsonAsync<List<DailyAttendanceResponse>>();

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
                new Punch { Id = Guid.NewGuid(), TenantId = tenantA.Id, EmployeeId = employeeA.Id, StationId = stationA.Id, PunchType = PunchType.In, Timestamp = day.ToDateTime(new TimeOnly(8, 0)) },
                new Punch { Id = Guid.NewGuid(), TenantId = tenantA.Id, EmployeeId = employeeA.Id, StationId = stationA.Id, PunchType = PunchType.Out, Timestamp = day.ToDateTime(new TimeOnly(16, 0)) },
                new Punch { Id = Guid.NewGuid(), TenantId = tenantB.Id, EmployeeId = employeeB.Id, StationId = stationB.Id, PunchType = PunchType.In, Timestamp = day.ToDateTime(new TimeOnly(9, 0)) },
                new Punch { Id = Guid.NewGuid(), TenantId = tenantB.Id, EmployeeId = employeeB.Id, StationId = stationB.Id, PunchType = PunchType.Out, Timestamp = day.ToDateTime(new TimeOnly(17, 0)) });
            db.SaveChanges();

            tenantAId = tenantA.Id;
            employeeAId = employeeA.Id;
            employeeBId = employeeB.Id;
        }

        var client = _factory.CreateClient();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hasher = new PasswordHasher<User>();
            var user = new User { Email = $"admin-{Guid.NewGuid()}@zak.test", PasswordHash = "", Role = UserRole.TenantAdmin, TenantId = tenantAId };
            user.PasswordHash = hasher.HashPassword(user, "correct-horse");
            db.Users.Add(user);
            db.SaveChanges();

            var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, "correct-horse"));
            var body = await login.Content.ReadFromJsonAsync<LoginResponse>();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Token);
        }

        var response = await client.GetAsync("/api/attendance/daily?date=2026-08-11");
        var rows = await response.Content.ReadFromJsonAsync<List<DailyAttendanceResponse>>();

        Assert.Contains(rows!, r => r.EmployeeId == employeeAId);
        Assert.DoesNotContain(rows!, r => r.EmployeeId == employeeBId);
    }
}
