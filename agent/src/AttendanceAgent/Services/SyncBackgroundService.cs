using System.Net.Http;
using AttendanceAgent.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AttendanceAgent.Services;

public class SyncBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SyncBackgroundService> _logger;
    private readonly TimeSpan _interval;

    public SyncBackgroundService(IServiceScopeFactory scopeFactory, ILogger<SyncBackgroundService> logger, TimeSpan? interval = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
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
        // Widened beyond HttpRequestException for the same reason as EmployeeDirectoryService/
        // TemplateCacheService: once the host is actually started (see App.xaml.cs), an uncaught
        // exception here would fault BackgroundService.ExecuteAsync and, under the default
        // BackgroundServiceExceptionBehavior.StopHost, permanently kill the sync loop for the rest
        // of the process's life with no retry, ever again.
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Punch sync failed against the backend; leaving {Count} punch(es) queued for retry.", pending.Count);
        }
    }
}
