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
        // Widened to wrap the whole method — scope creation, both GetRequiredService calls, and
        // GetPendingAsync included — not just the submit call. Any exception from those (e.g. a
        // SqliteException because the schema doesn't exist yet, or any other DB-layer failure) was
        // previously unguarded and would propagate out of FlushOnceAsync into ExecuteAsync's
        // unguarded `await FlushOnceAsync(stoppingToken)` call, faulting BackgroundService and, under
        // the default BackgroundServiceExceptionBehavior.StopHost, permanently killing the sync loop
        // for the rest of the process's life with no retry, ever again.
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var queue = scope.ServiceProvider.GetRequiredService<IPunchQueueService>();
            var api = scope.ServiceProvider.GetRequiredService<IBackendApiClient>();

            // Capped so a queue built up over a multi-day outage can't post an unbounded batch in
            // one go; anything beyond the cap is simply left queued and picked up on a later tick.
            var pending = await queue.GetPendingAsync(ct);
            if (pending.Count == 0) return;

            var result = await api.SubmitPunchesAsync(pending, ct);
            switch (result)
            {
                case PunchBatchSubmitResult.Accepted:
                    await queue.RemoveSyncedAsync(pending.Select(p => p.Id), ct);
                    break;

                case PunchBatchSubmitResult.RejectedByBackend:
                    // The backend rejects the WHOLE batch (400) if any single punch in it is
                    // permanently invalid (e.g. the employee was deleted server-side while this
                    // station was offline). Retrying the same batch forever would wedge every
                    // subsequent punch behind that one poison punch, with no cap and no visibility.
                    // Dropping this specific batch is a deliberate, lossy-but-bounded tradeoff for
                    // Phase 1 — logged loudly so it's visible, not silent.
                    _logger.LogWarning(
                        "Backend rejected a batch of {Count} punch(es) as malformed (HTTP 400); dropping them as unsyncable to avoid wedging the queue. Ids: {PunchIds}",
                        pending.Count, string.Join(", ", pending.Select(p => p.Id)));
                    await queue.RemoveSyncedAsync(pending.Select(p => p.Id), ct);
                    break;

                case PunchBatchSubmitResult.TransientFailure:
                    // Leave the batch queued; worth retrying on the next tick.
                    break;
            }
        }
        // Widened beyond HttpRequestException for the same reason as EmployeeDirectoryService/
        // TemplateCacheService: once the host is actually started (see App.xaml.cs), an uncaught
        // exception here would fault BackgroundService.ExecuteAsync and, under the default
        // BackgroundServiceExceptionBehavior.StopHost, permanently kill the sync loop for the rest
        // of the process's life with no retry, ever again.
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Punch sync failed against the backend; leaving punch(es) queued for retry.");
        }
    }
}
