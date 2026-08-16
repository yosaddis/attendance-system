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

    private (Guid TenantId, Guid EmployeeId, string StationKey) SeedTenantEmployeeAndStation(string suffix = "A")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = new Tenant { Name = $"Acme Foods {suffix}", DeviceVendor = DeviceVendor.Zk4500 };
        var employee = new Employee { TenantId = tenant.Id, EmployeeCode = $"E001-{suffix}", Name = "Jane Doe" };
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

    [Fact]
    public async Task Fetch_ForOtherTenantsEmployee_ReturnsNotFound()
    {
        var (_, employeeIdA, stationKeyA) = SeedTenantEmployeeAndStation("A");
        var (_, _, stationKeyB) = SeedTenantEmployeeAndStation("B");

        var enrollClient = _factory.CreateClient();
        enrollClient.DefaultRequestHeaders.Add("X-Station-Key", stationKeyA);
        var templateBytes = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5 });
        var enroll = await enrollClient.PostAsJsonAsync("/api/templates", new EnrollTemplateRequest(employeeIdA, templateBytes));
        Assert.Equal(HttpStatusCode.OK, enroll.StatusCode);

        var otherTenantClient = _factory.CreateClient();
        otherTenantClient.DefaultRequestHeaders.Add("X-Station-Key", stationKeyB);
        var fetch = await otherTenantClient.GetAsync($"/api/templates/{employeeIdA}");

        Assert.Equal(HttpStatusCode.NotFound, fetch.StatusCode);
    }

    [Fact]
    public async Task Fetch_ForNonexistentEmployee_ReturnsNotFound()
    {
        var (_, _, stationKey) = SeedTenantEmployeeAndStation();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Station-Key", stationKey);

        var fetch = await client.GetAsync($"/api/templates/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, fetch.StatusCode);
    }

    [Fact]
    public async Task Enroll_WithMalformedBase64_ReturnsBadRequest()
    {
        var (_, employeeId, stationKey) = SeedTenantEmployeeAndStation();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Station-Key", stationKey);

        var enroll = await client.PostAsJsonAsync("/api/templates",
            new EnrollTemplateRequest(employeeId, "not-valid-base64!!!"));

        Assert.Equal(HttpStatusCode.BadRequest, enroll.StatusCode);
    }

    [Fact]
    public async Task Enroll_ForOtherTenantsEmployee_ReturnsNotFound()
    {
        var (_, employeeIdA, _) = SeedTenantEmployeeAndStation("A");
        var (_, _, stationKeyB) = SeedTenantEmployeeAndStation("B");

        var otherTenantClient = _factory.CreateClient();
        otherTenantClient.DefaultRequestHeaders.Add("X-Station-Key", stationKeyB);
        var templateBytes = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5 });
        var enroll = await otherTenantClient.PostAsJsonAsync("/api/templates", new EnrollTemplateRequest(employeeIdA, templateBytes));

        Assert.Equal(HttpStatusCode.NotFound, enroll.StatusCode);
    }
}
