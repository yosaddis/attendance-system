using System.Net;
using System.Net.Http.Json;
using AttendanceApi.Data;
using AttendanceApi.Dtos;
using AttendanceApi.Entities;
using AttendanceApi.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AttendanceApi.Tests;

public class HealthControllerTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public HealthControllerTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetForStation_ReturnsStationIdAndTenantId()
    {
        Guid tenantId, stationId;
        string stationKey;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = new Tenant { Name = "Acme Foods", DeviceVendor = DeviceVendor.Zk4500 };
            var (plaintextKey, hash) = StationKeyGenerator.Generate();
            var station = new Station { TenantId = tenant.Id, Name = "Front Desk", DeviceVendor = DeviceVendor.Zk4500, ApiKeyHash = hash };
            db.Tenants.Add(tenant);
            db.Stations.Add(station);
            db.SaveChanges();
            tenantId = tenant.Id;
            stationId = station.Id;
            stationKey = plaintextKey;
        }
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Station-Key", stationKey);

        var response = await client.GetAsync("/api/health/station");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<StationHealthResponse>();
        Assert.Equal(tenantId, body!.TenantId);
        Assert.Equal(stationId, body.StationId);
        Assert.Equal("ok", body.Status);
    }

    [Fact]
    public async Task GetForStation_WithoutStationKey_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/health/station");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
