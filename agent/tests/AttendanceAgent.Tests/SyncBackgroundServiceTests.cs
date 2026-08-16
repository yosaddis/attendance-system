using AttendanceAgent.Api;
using AttendanceAgent.Data;
using AttendanceAgent.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
        var api = new FakeBackendApiClient { SubmitResult = true };
        var provider = BuildProvider(api);
        using (var scope = provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPunchQueueService>()
                .EnqueueAsync(Guid.NewGuid(), "In", DateTimeOffset.UtcNow);
        }

        var sync = new SyncBackgroundService(provider.GetRequiredService<IServiceScopeFactory>());
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

        var sync = new SyncBackgroundService(provider.GetRequiredService<IServiceScopeFactory>());
        await sync.FlushOnceAsync(CancellationToken.None);

        using var verifyScope = provider.CreateScope();
        var remaining = await verifyScope.ServiceProvider.GetRequiredService<IPunchQueueService>().GetPendingAsync();
        Assert.Single(remaining);
    }

    [Fact]
    public async Task FlushOnce_ExplicitRejection_LeavesQueueIntact()
    {
        var api = new FakeBackendApiClient { SubmitResult = false };
        var provider = BuildProvider(api);
        using (var scope = provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPunchQueueService>()
                .EnqueueAsync(Guid.NewGuid(), "In", DateTimeOffset.UtcNow);
        }

        var sync = new SyncBackgroundService(provider.GetRequiredService<IServiceScopeFactory>());
        await sync.FlushOnceAsync(CancellationToken.None);

        using var verifyScope = provider.CreateScope();
        var remaining = await verifyScope.ServiceProvider.GetRequiredService<IPunchQueueService>().GetPendingAsync();
        Assert.Single(remaining);
    }
}
