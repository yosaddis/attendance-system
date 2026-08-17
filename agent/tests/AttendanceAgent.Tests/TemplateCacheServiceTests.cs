using AttendanceAgent.Data;
using AttendanceAgent.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AttendanceAgent.Tests;

public class TemplateCacheServiceTests
{
    private static readonly Guid EmployeeId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task GetTemplateAsync_Online_CachesResultLocally()
    {
        using var db = TestDb.CreateInMemory();
        var api = new FakeBackendApiClient { TemplateResult = new byte[] { 1, 2, 3 } };
        var service = new TemplateCacheService(api, db, NullLogger<TemplateCacheService>.Instance);

        var result = await service.GetTemplateAsync(EmployeeId);

        Assert.Equal(new byte[] { 1, 2, 3 }, result);
        Assert.NotNull(await db.CachedTemplates.FindAsync(EmployeeId));
    }

    [Fact]
    public async Task GetTemplateAsync_Offline_FallsBackToLocalCache()
    {
        using var db = TestDb.CreateInMemory();
        db.CachedTemplates.Add(new CachedTemplate { EmployeeId = EmployeeId, TemplateData = new byte[] { 4, 5, 6 }, CachedAt = DateTimeOffset.UtcNow });
        db.SaveChanges();
        var api = new FakeBackendApiClient { ThrowOnTemplate = true };
        var service = new TemplateCacheService(api, db, NullLogger<TemplateCacheService>.Instance);

        var result = await service.GetTemplateAsync(EmployeeId);

        Assert.Equal(new byte[] { 4, 5, 6 }, result);
    }

    [Fact]
    public async Task GetTemplateAsync_OfflineAndUncached_ReturnsNull()
    {
        using var db = TestDb.CreateInMemory();
        var api = new FakeBackendApiClient { ThrowOnTemplate = true };
        var service = new TemplateCacheService(api, db, NullLogger<TemplateCacheService>.Instance);

        var result = await service.GetTemplateAsync(EmployeeId);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetTemplateAsync_MalformedResponse_FallsBackToLocalCache()
    {
        // e.g. a captive portal returning HTML with a 200, or a corrupt base64 template body —
        // these surface as JsonException/FormatException, not HttpRequestException.
        using var db = TestDb.CreateInMemory();
        db.CachedTemplates.Add(new CachedTemplate { EmployeeId = EmployeeId, TemplateData = new byte[] { 4, 5, 6 }, CachedAt = DateTimeOffset.UtcNow });
        db.SaveChanges();
        var api = new FakeBackendApiClient { TemplateExceptionToThrow = () => new FormatException("invalid base64") };
        var service = new TemplateCacheService(api, db, NullLogger<TemplateCacheService>.Instance);

        var result = await service.GetTemplateAsync(EmployeeId);

        Assert.Equal(new byte[] { 4, 5, 6 }, result);
    }

    [Fact]
    public async Task GetTemplateAsync_DefinitiveNotFound_PurgesStaleCacheAndReturnsNull()
    {
        // The backend call SUCCEEDED (no exception) and returned null — a definitive "no template
        // for this employee anymore" signal (e.g. the employee/template was deleted server-side),
        // not "backend unreachable." Must NOT fall through to the stale cached template; must purge
        // it instead.
        using var db = TestDb.CreateInMemory();
        db.CachedTemplates.Add(new CachedTemplate { EmployeeId = EmployeeId, TemplateData = new byte[] { 4, 5, 6 }, CachedAt = DateTimeOffset.UtcNow });
        db.SaveChanges();
        var api = new FakeBackendApiClient { TemplateResult = null };
        var service = new TemplateCacheService(api, db, NullLogger<TemplateCacheService>.Instance);

        var result = await service.GetTemplateAsync(EmployeeId);

        Assert.Null(result);
        Assert.Null(await db.CachedTemplates.FindAsync(EmployeeId));
    }

    [Fact]
    public async Task GetTemplateAsync_CallerRequestedCancellation_Propagates()
    {
        // See EmployeeDirectoryServiceTests.ResolveAsync_CallerRequestedCancellation_Propagates for
        // why the token is cancelled from *inside* the fake's throwing method (right before it
        // throws a plain InvalidOperationException) rather than pre-cancelled before the call.
        // Pre-cancelling and throwing OperationCanceledException wouldn't discriminate here either:
        // the fallback cache-read below (`_db.CachedTemplates.FindAsync(..., ct)`) also throws
        // OperationCanceledException once ct is cancelled, so the test would pass identically even
        // with the `when (!ct.IsCancellationRequested)` guard removed. With this shape, an intact
        // guard propagates the InvalidOperationException immediately (fallback never runs); a
        // removed guard would swallow it and let the fallback throw OperationCanceledException
        // instead — a different exception type that fails this assertion.
        using var db = TestDb.CreateInMemory();
        using var cts = new CancellationTokenSource();
        var api = new FakeBackendApiClient
        {
            TemplateExceptionToThrow = () =>
            {
                cts.Cancel();
                return new InvalidOperationException("simulated failure");
            },
        };
        var service = new TemplateCacheService(api, db, NullLogger<TemplateCacheService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetTemplateAsync(EmployeeId, cts.Token));
    }
}
