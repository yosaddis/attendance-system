using AttendanceAgent.Api;
using AttendanceAgent.Data;
using AttendanceAgent.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AttendanceAgent.Tests;

public class SyncBackgroundServiceTests
{
    private static ServiceProvider BuildProvider(FakeBackendApiClient api)
    {
        var services = new ServiceCollection();
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        services.AddDbContext<AgentDbContext>(o => o.UseSqlite(connection));
        services.AddSingleton<IBackendApiClient>(api);
        services.AddScoped<IPunchQueueService, PunchQueueService>();
        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<AgentDbContext>().Database.EnsureCreated();
        return provider;
    }

    [Fact]
    public async Task FlushOnce_Online_SubmitsAndClearsQueue()
    {
        var api = new FakeBackendApiClient { SubmitResult = PunchBatchSubmitResult.Accepted };
        var provider = BuildProvider(api);
        using (var scope = provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPunchQueueService>()
                .EnqueueAsync(Guid.NewGuid(), "In", DateTimeOffset.UtcNow);
        }

        var sync = new SyncBackgroundService(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<SyncBackgroundService>.Instance);
        await sync.FlushOnceAsync(CancellationToken.None);

        using var verifyScope = provider.CreateScope();
        var remaining = await verifyScope.ServiceProvider.GetRequiredService<IPunchQueueService>().GetPendingAsync();
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task FlushOnce_Offline_LeavesQueueIntact()
    {
        var api = new FakeBackendApiClient { ThrowOnSubmit = true };
        var provider = BuildProvider(api);
        using (var scope = provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPunchQueueService>()
                .EnqueueAsync(Guid.NewGuid(), "In", DateTimeOffset.UtcNow);
        }

        var sync = new SyncBackgroundService(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<SyncBackgroundService>.Instance);
        await sync.FlushOnceAsync(CancellationToken.None);

        using var verifyScope = provider.CreateScope();
        var remaining = await verifyScope.ServiceProvider.GetRequiredService<IPunchQueueService>().GetPendingAsync();
        Assert.Single(remaining);
    }

    [Fact]
    public async Task FlushOnce_HttpTimeout_LeavesQueueIntact()
    {
        // TaskCanceledException (HttpClient timeout) must be treated as "still offline" too, not
        // just HttpRequestException — otherwise it faults BackgroundService.ExecuteAsync and, under
        // the default BackgroundServiceExceptionBehavior.StopHost, kills the sync loop forever.
        var api = new FakeBackendApiClient { SubmitExceptionToThrow = () => new TaskCanceledException("timed out") };
        var provider = BuildProvider(api);
        using (var scope = provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPunchQueueService>()
                .EnqueueAsync(Guid.NewGuid(), "In", DateTimeOffset.UtcNow);
        }

        var sync = new SyncBackgroundService(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<SyncBackgroundService>.Instance);
        await sync.FlushOnceAsync(CancellationToken.None);

        using var verifyScope = provider.CreateScope();
        var remaining = await verifyScope.ServiceProvider.GetRequiredService<IPunchQueueService>().GetPendingAsync();
        Assert.Single(remaining);
    }

    [Fact]
    public async Task FlushOnce_CallerRequestedCancellation_Propagates()
    {
        var api = new FakeBackendApiClient();
        var provider = BuildProvider(api);
        using (var scope = provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPunchQueueService>()
                .EnqueueAsync(Guid.NewGuid(), "In", DateTimeOffset.UtcNow);
        }
        using var cts = new CancellationTokenSource();
        // Cancel only once we're inside the submit call (not before), so GetPendingAsync(ct) above
        // still runs against a live token and we genuinely exercise the `when (!ct.IsCancellationRequested)`
        // filter around the submit — not just an earlier, unrelated cancellation check.
        api.SubmitExceptionToThrow = () =>
        {
            cts.Cancel();
            return new OperationCanceledException(cts.Token);
        };

        var sync = new SyncBackgroundService(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<SyncBackgroundService>.Instance);

        await Assert.ThrowsAsync<OperationCanceledException>(() => sync.FlushOnceAsync(cts.Token));
    }

    [Fact]
    public async Task FlushOnce_TransientFailure_LeavesQueueIntact()
    {
        // A 5xx/transient failure is worth retrying as-is — the whole batch must stay queued.
        var api = new FakeBackendApiClient { SubmitResult = PunchBatchSubmitResult.TransientFailure };
        var provider = BuildProvider(api);
        using (var scope = provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPunchQueueService>()
                .EnqueueAsync(Guid.NewGuid(), "In", DateTimeOffset.UtcNow);
        }

        var sync = new SyncBackgroundService(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<SyncBackgroundService>.Instance);
        await sync.FlushOnceAsync(CancellationToken.None);

        using var verifyScope = provider.CreateScope();
        var remaining = await verifyScope.ServiceProvider.GetRequiredService<IPunchQueueService>().GetPendingAsync();
        Assert.Single(remaining);
    }

    [Fact]
    public async Task FlushOnce_PermanentRejection_DropsThePoisonBatchInstead()
    {
        // A definitive HTTP-400 rejection means retrying the same batch will fail forever — e.g. a
        // punch for an employee deleted server-side while this station was offline. Without this,
        // that single poison punch would wedge the queue forever, blocking every punch after it
        // with no cap and no visibility. The fix: drop this batch (logged loudly) so subsequent
        // valid punches aren't blocked — a deliberate, bounded tradeoff over an unbounded stall.
        var api = new FakeBackendApiClient { SubmitResult = PunchBatchSubmitResult.RejectedByBackend };
        var provider = BuildProvider(api);
        using (var scope = provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPunchQueueService>()
                .EnqueueAsync(Guid.NewGuid(), "In", DateTimeOffset.UtcNow);
        }

        var sync = new SyncBackgroundService(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<SyncBackgroundService>.Instance);
        await sync.FlushOnceAsync(CancellationToken.None);

        using var verifyScope = provider.CreateScope();
        var remaining = await verifyScope.ServiceProvider.GetRequiredService<IPunchQueueService>().GetPendingAsync();
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task FlushOnce_PermanentRejection_DropsOnlyThatBatch_LaterValidPunchesStillSyncOnNextTick()
    {
        // Proves the fix is scoped to "this failed batch," not "the whole queue forever": after a
        // poison batch is dropped, a subsequently-enqueued valid punch still syncs normally on the
        // next flush.
        var api = new FakeBackendApiClient { SubmitResult = PunchBatchSubmitResult.RejectedByBackend };
        var provider = BuildProvider(api);
        using (var scope = provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPunchQueueService>()
                .EnqueueAsync(Guid.NewGuid(), "In", DateTimeOffset.UtcNow);
        }

        var sync = new SyncBackgroundService(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<SyncBackgroundService>.Instance);
        await sync.FlushOnceAsync(CancellationToken.None);

        using (var verifyScope = provider.CreateScope())
        {
            Assert.Empty(await verifyScope.ServiceProvider.GetRequiredService<IPunchQueueService>().GetPendingAsync());
        }

        api.SubmitResult = PunchBatchSubmitResult.Accepted;
        using (var scope = provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPunchQueueService>()
                .EnqueueAsync(Guid.NewGuid(), "In", DateTimeOffset.UtcNow);
        }

        await sync.FlushOnceAsync(CancellationToken.None);

        using var finalScope = provider.CreateScope();
        Assert.Empty(await finalScope.ServiceProvider.GetRequiredService<IPunchQueueService>().GetPendingAsync());
    }
}
