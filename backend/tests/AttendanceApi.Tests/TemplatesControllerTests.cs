using System.Net;
using System.Net.Http.Json;
using AttendanceApi.Data;
using AttendanceApi.Dtos;
using AttendanceApi.Entities;
using AttendanceApi.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AttendanceApi.Tests;

public class TemplatesControllerTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public TemplatesControllerTests(ApiFactory factory)
    {
        _factory = factory;
    }

    private (Guid TenantId, Guid EmployeeId, string StationKey) SeedTenantEmployeeAndStation()
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
        return (tenant.Id, employee.Id, plaintextKey);
    }

    [Fact]
    public async Task EnrollThenFetch_RoundTripsPlaintextTemplate()
    {
        var (_, employeeId, stationKey) = SeedTenantEmployeeAndStation();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Station-Key", stationKey);

        var templateBytes = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5 });
        var enroll = await client.PostAsJsonAsync("/api/templates", new EnrollTemplateRequest(employeeId, templateBytes));
        Assert.Equal(HttpStatusCode.OK, enroll.StatusCode);

        var fetch = await client.GetAsync($"/api/templates/{employeeId}");
        Assert.Equal(HttpStatusCode.OK, fetch.StatusCode);
        var body = await fetch.Content.ReadFromJsonAsync<TemplateResponse>();

        Assert.Equal(templateBytes, body!.TemplateData);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = db.FingerprintTemplates.Single(t => t.EmployeeId == employeeId);
        Assert.NotEqual(Convert.FromBase64String(templateBytes), stored.TemplateDataEncrypted);
    }
}
