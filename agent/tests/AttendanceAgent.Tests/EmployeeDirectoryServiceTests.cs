using AttendanceAgent.Api;
using AttendanceAgent.Data;
using AttendanceAgent.Services;
using Microsoft.Extensions.Logging.Abstractions;
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
        var service = new EmployeeDirectoryService(api, db, NullLogger<EmployeeDirectoryService>.Instance);

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
        var service = new EmployeeDirectoryService(api, db, NullLogger<EmployeeDirectoryService>.Instance);

        var result = await service.ResolveAsync("E001");

        Assert.Equal("Jane Doe", result!.Name);
    }

    [Fact]
    public async Task ResolveAsync_OfflineAndUncached_ReturnsNull()
    {
        using var db = TestDb.CreateInMemory();
        var api = new FakeBackendApiClient { ThrowOnLookup = true };
        var service = new EmployeeDirectoryService(api, db, NullLogger<EmployeeDirectoryService>.Instance);

        var result = await service.ResolveAsync("E001");

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveAsync_HttpTimeout_FallsBackToLocalCache()
    {
        // TaskCanceledException is what a real HttpClient throws on timeout — NOT an
        // HttpRequestException in modern .NET. This must be treated as "backend unreachable" too.
        using var db = TestDb.CreateInMemory();
        db.CachedEmployees.Add(new CachedEmployee { EmployeeId = EmployeeId, EmployeeCode = "E001", Name = "Jane Doe", CachedAt = DateTimeOffset.UtcNow });
        db.SaveChanges();
        var api = new FakeBackendApiClient { LookupExceptionToThrow = () => new TaskCanceledException("timed out") };
        var service = new EmployeeDirectoryService(api, db, NullLogger<EmployeeDirectoryService>.Instance);

        var result = await service.ResolveAsync("E001");

        Assert.Equal("Jane Doe", result!.Name);
    }

    [Fact]
    public async Task ResolveAsync_CallerRequestedCancellation_Propagates()
    {
        // A deliberate caller-requested cancellation (e.g. app shutdown) must NOT be swallowed as
        // "offline" — it should propagate normally.
        using var db = TestDb.CreateInMemory();
        using var cts = new CancellationTokenSource();
        var api = new FakeBackendApiClient { LookupExceptionToThrow = () => new OperationCanceledException(cts.Token) };
        var service = new EmployeeDirectoryService(api, db, NullLogger<EmployeeDirectoryService>.Instance);
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.ResolveAsync("E001", cts.Token));
    }
}
