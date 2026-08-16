using System.Net;
using System.Net.Http.Json;
using AttendanceApi.Data;
using AttendanceApi.Dtos;
using AttendanceApi.Entities;
using AttendanceApi.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AttendanceApi.Tests;

public class PunchesControllerTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public PunchesControllerTests(ApiFactory factory)
    {
        _factory = factory;
    }

    private (Guid EmployeeId, string StationKey) SeedTenantEmployeeAndStation()
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
        return (employee.Id, plaintextKey);
    }

    [Fact]
    public async Task BatchIngest_IsIdempotentByPunchId()
    {
        var (employeeId, stationKey) = SeedTenantEmployeeAndStation();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Station-Key", stationKey);

        var punchId = Guid.NewGuid();
        var request = new PunchBatchRequest(new List<PunchDto>
        {
            new(punchId, employeeId, "In", DateTimeOffset.UtcNow),
        });

        var first = await client.PostAsJsonAsync("/api/punches/batch", request);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var retry = await client.PostAsJsonAsync("/api/punches/batch", request);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Single(db.Punches, p => p.Id == punchId);
    }

    [Fact]
    public async Task BatchIngest_WithOneInvalidPunchType_RejectsWholeBatchAndPersistsNothing()
    {
        var (employeeId, stationKey) = SeedTenantEmployeeAndStation();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Station-Key", stationKey);

        var validPunchId = Guid.NewGuid();
        var invalidPunchId = Guid.NewGuid();
        var request = new PunchBatchRequest(new List<PunchDto>
        {
            new(validPunchId, employeeId, "In", DateTimeOffset.UtcNow),
            new(invalidPunchId, employeeId, "NotARealPunchType", DateTimeOffset.UtcNow),
        });

        var response = await client.PostAsJsonAsync("/api/punches/batch", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.DoesNotContain(db.Punches, p => p.Id == validPunchId);
        Assert.DoesNotContain(db.Punches, p => p.Id == invalidPunchId);
    }

    [Fact]
    public async Task BatchIngest_WithDuplicateIdWithinBatch_RejectsWholeBatchAndPersistsNothing()
    {
        var (employeeId, stationKey) = SeedTenantEmployeeAndStation();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Station-Key", stationKey);

        var duplicatedId = Guid.NewGuid();
        var request = new PunchBatchRequest(new List<PunchDto>
        {
            new(duplicatedId, employeeId, "In", DateTimeOffset.UtcNow),
            new(duplicatedId, employeeId, "Out", DateTimeOffset.UtcNow),
        });

        var response = await client.PostAsJsonAsync("/api/punches/batch", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.DoesNotContain(db.Punches, p => p.Id == duplicatedId);
    }
}
