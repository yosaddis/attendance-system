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
        //
        // The token is cancelled from *inside* the fake's throwing method — right before it throws
        // a plain InvalidOperationException — rather than pre-cancelled before the call. This is
        // deliberate: pre-cancelling the token and throwing an OperationCanceledException wouldn't
        // discriminate, because the fallback cache-read below (`SingleOrDefaultAsync(..., ct)`)
        // *also* throws OperationCanceledException once ct is cancelled, for unrelated reasons — so
        // the test would pass identically even if the `when (!ct.IsCancellationRequested)` guard
        // were deleted entirely (catch would just fire, log, then the fallback throws the same
        // exception type anyway).
        //
        // With this shape: if the guard is intact, ct is already cancelled by the time the `when`
        // filter runs, so it does NOT catch — the InvalidOperationException propagates immediately
        // and the fallback code never executes. If the guard were removed (unconditional catch),
        // the InvalidOperationException would be swallowed and the fallback would run instead,
        // throwing OperationCanceledException — a different exception type — which fails this
        // assertion. So this test genuinely fails if the guard regresses.
        using var db = TestDb.CreateInMemory();
        using var cts = new CancellationTokenSource();
        var api = new FakeBackendApiClient
        {
            LookupExceptionToThrow = () =>
            {
                cts.Cancel();
                return new InvalidOperationException("simulated failure");
            },
        };
        var service = new EmployeeDirectoryService(api, db, NullLogger<EmployeeDirectoryService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResolveAsync("E001", cts.Token));
    }
}
