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
    public async Task GetTemplateAsync_CallerRequestedCancellation_Propagates()
    {
        using var db = TestDb.CreateInMemory();
        using var cts = new CancellationTokenSource();
        var api = new FakeBackendApiClient { TemplateExceptionToThrow = () => new OperationCanceledException(cts.Token) };
        var service = new TemplateCacheService(api, db, NullLogger<TemplateCacheService>.Instance);
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.GetTemplateAsync(EmployeeId, cts.Token));
    }
}
