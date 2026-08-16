using System.Net;
using System.Net.Http.Json;
using AttendanceApi.Data;
using AttendanceApi.Dtos;
using AttendanceApi.Entities;
using AttendanceApi.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AttendanceApi.Tests;

public class EmployeeLookupControllerTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public EmployeeLookupControllerTests(ApiFactory factory)
    {
        _factory = factory;
    }

    private string SeedTenantEmployeeAndStation(out Guid employeeId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = new Tenant { Name = "Acme Foods", DeviceVendor = DeviceVendor.Zk4500 };
        var employee = new Employee { TenantId = tenant.Id, EmployeeCode = "E001", Name = "Jane Doe" };
        var (plaintextKey, hash) = StationKeyGenerator.Generate();
        var station = new Station { TenantId = tenant.Id, Name = "Front Desk", DeviceVendor = DeviceVendor.Zk4500, ApiKeyHash = hash };
        db.Tenants.Add(tenant);
        db.Employees.Add(employee);
        db.Stations.Add(station);
        db.SaveChanges();
        employeeId = employee.Id;
        return plaintextKey;
    }

    [Fact]
    public async Task Lookup_ByCode_ReturnsEmployeeId()
    {
        var stationKey = SeedTenantEmployeeAndStation(out var employeeId);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Station-Key", stationKey);

        var response = await client.GetAsync("/api/employees/lookup?code=E001");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<EmployeeLookupResponse>();
        Assert.Equal(employeeId, body!.EmployeeId);
    }

    [Fact]
    public async Task Lookup_UnknownCode_ReturnsNotFound()
    {
        var stationKey = SeedTenantEmployeeAndStation(out _);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Station-Key", stationKey);

        var response = await client.GetAsync("/api/employees/lookup?code=NOPE");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Lookup_SameCodeInOtherTenant_ReturnsOwnTenantsEmployee()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var otherTenant = new Tenant { Name = "Other Foods", DeviceVendor = DeviceVendor.Zk4500 };
            var otherEmployee = new Employee { TenantId = otherTenant.Id, EmployeeCode = "E001", Name = "Someone Else" };
            db.Tenants.Add(otherTenant);
            db.Employees.Add(otherEmployee);
            db.SaveChanges();
        }

        var stationKey = SeedTenantEmployeeAndStation(out var employeeId);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Station-Key", stationKey);

        var response = await client.GetAsync("/api/employees/lookup?code=E001");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<EmployeeLookupResponse>();
        Assert.Equal(employeeId, body!.EmployeeId);
    }

    [Fact]
    public async Task Lookup_CodeThatOnlyExistsInAnotherTenant_ReturnsNotFound()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var otherTenant = new Tenant { Name = "Other Foods", DeviceVendor = DeviceVendor.Zk4500 };
            var otherEmployee = new Employee { TenantId = otherTenant.Id, EmployeeCode = "ONLY-OTHER", Name = "Someone Else" };
            db.Tenants.Add(otherTenant);
            db.Employees.Add(otherEmployee);
            db.SaveChanges();
        }

        var stationKey = SeedTenantEmployeeAndStation(out _);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Station-Key", stationKey);

        var response = await client.GetAsync("/api/employees/lookup?code=ONLY-OTHER");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Lookup_WithoutStationKey_ReturnsUnauthorized()
    {
        SeedTenantEmployeeAndStation(out _);
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/employees/lookup?code=E001");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
