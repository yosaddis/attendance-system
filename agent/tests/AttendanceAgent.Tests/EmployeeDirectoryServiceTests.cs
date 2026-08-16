using AttendanceAgent.Api;
using AttendanceAgent.Data;
using AttendanceAgent.Services;
using Xunit;

namespace AttendanceAgent.Tests;

public class EmployeeDirectoryServiceTests
{
    private static readonly Guid EmployeeId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task ResolveAsync_Online_CachesResultLocally()
    {
        using var db = TestDb.CreateInMemory();
        var api = new FakeBackendApiClient { LookupResult = new EmployeeLookupResult(EmployeeId, "E001", "Jane Doe") };
        var service = new EmployeeDirectoryService(api, db);

        var result = await service.ResolveAsync("E001");

        Assert.Equal("Jane Doe", result!.Name);
        Assert.NotNull(await db.CachedEmployees.FindAsync(EmployeeId));
    }

    [Fact]
    public async Task ResolveAsync_Offline_FallsBackToLocalCache()
    {
        using var db = TestDb.CreateInMemory();
        db.CachedEmployees.Add(new CachedEmployee { EmployeeId = EmployeeId, EmployeeCode = "E001", Name = "Jane Doe", CachedAt = DateTimeOffset.UtcNow });
        db.SaveChanges();
        var api = new FakeBackendApiClient { ThrowOnLookup = true };
        var service = new EmployeeDirectoryService(api, db);

        var result = await service.ResolveAsync("E001");

        Assert.Equal("Jane Doe", result!.Name);
    }

    [Fact]
    public async Task ResolveAsync_OfflineAndUncached_ReturnsNull()
    {
        using var db = TestDb.CreateInMemory();
        var api = new FakeBackendApiClient { ThrowOnLookup = true };
        var service = new EmployeeDirectoryService(api, db);

        var result = await service.ResolveAsync("E001");

        Assert.Null(result);
    }
}
