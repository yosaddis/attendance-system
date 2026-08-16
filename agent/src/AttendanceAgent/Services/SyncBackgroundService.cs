using System.Net.Http;
using AttendanceAgent.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AttendanceAgent.Services;

public class SyncBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeSpan _interval;

    public SyncBackgroundService(IServiceScopeFactory scopeFactory, TimeSpan? interval = null)
    {
        _scopeFactory = scopeFactory;
        _interval = interval ?? TimeSpan.FromSeconds(30);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await FlushOnceAsync(stoppingToken);
            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // shutting down
            }
        }
    }

    public async Task FlushOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IPunchQueueService>();
        var api = scope.ServiceProvider.GetRequiredService<IBackendApiClient>();

        var pending = await queue.GetPendingAsync(ct);
        if (pending.Count == 0) return;

        try
        {
            var accepted = await api.SubmitPunchesAsync(pending, ct);
            if (accepted)
                await queue.RemoveSyncedAsync(pending.Select(p => p.Id), ct);
        }
        catch (HttpRequestException)
        {
            // still offline — leave the queue untouched, retry next tick
        }
    }
}
