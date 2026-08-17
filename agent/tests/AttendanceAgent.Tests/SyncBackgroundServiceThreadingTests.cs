using AttendanceAgent.Api;
using AttendanceAgent.Data;
using AttendanceAgent.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AttendanceAgent.Tests;

/// <summary>
/// Mechanical proxy for the App.xaml.cs fix that changed `await _host.StartAsync();` to
/// `await Task.Run(() => _host!.StartAsync());`. A full WPF-level reproduction isn't feasible in
/// this (headless) environment, but the underlying mechanism is: SynchronizationContext.Current
/// is a plain [ThreadStatic] on the CLR — it never flows across a `Task.Run` boundary onto a
/// thread-pool thread. So calling `BackgroundService.StartAsync` (which synchronously kicks off
/// `ExecuteAsync` up to its first await) via `Task.Run` guarantees `ExecuteAsync`'s subsequent
/// awaits (Task.Delay, EF queries, HTTP calls) capture a *null* ambient SynchronizationContext,
/// not the caller's — whereas awaiting `StartAsync` directly on a thread that has one installed
/// (e.g. WPF's DispatcherSynchronizationContext) would capture it, and every loop iteration would
/// try to marshal its continuation back to that thread.
///
/// This test installs a recording SynchronizationContext (standing in for the WPF dispatcher) on
/// a dedicated thread, starts a real SyncBackgroundService the same way App.xaml.cs does
/// (`Task.Run(() => host.StartAsync())`), lets its loop run for several intervals, and asserts
/// nothing ever tried to Post/Send back to that context — while independently confirming the loop
/// genuinely made progress (so the zero count isn't just "nothing ran").
/// </summary>
public class SyncBackgroundServiceThreadingTests
{
    [Fact]
    public async Task StartAsync_ViaTaskRun_LoopNeverMarshalsBackToCallersSynchronizationContext()
    {
        var flushCount = 0;
        var api = new CountingRejectingBackendApiClient(() => Interlocked.Increment(ref flushCount));

        var services = new ServiceCollection();
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        services.AddDbContext<AgentDbContext>(o => o.UseSqlite(connection));
        services.AddSingleton<IBackendApiClient>(api);
        services.AddScoped<IPunchQueueService, PunchQueueService>();
        using var provider = services.BuildServiceProvider();

        using (var scope = provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<AgentDbContext>().Database.EnsureCreated();
            await scope.ServiceProvider.GetRequiredService<IPunchQueueService>()
                .EnqueueAsync(Guid.NewGuid(), "In", DateTimeOffset.UtcNow);
        }

        // Short interval so several loop iterations happen well within the test's wait window.
        var sync = new SyncBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SyncBackgroundService>.Instance,
            TimeSpan.FromMilliseconds(15));

        var recorder = new RecordingSynchronizationContext();

        // Mirrors App.xaml.cs's OnStartup: install a SynchronizationContext on this thread (standing
        // in for the WPF dispatcher), then start the host the same way — via Task.Run, blocking
        // synchronously (GetAwaiter().GetResult(), not `await`) so this thread's own context is
        // never asked to pump anything as part of the start call itself.
        var startThread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(recorder);
            Task.Run(() => sync.StartAsync(CancellationToken.None)).GetAwaiter().GetResult();
        });
        startThread.Start();
        startThread.Join();

        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (Volatile.Read(ref flushCount) < 3 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            Assert.True(
                Volatile.Read(ref flushCount) >= 3,
                $"Expected the sync loop to make progress across several intervals; only observed {flushCount} flush attempt(s).");
        }
        finally
        {
            await sync.StopAsync(CancellationToken.None);
        }

        Assert.Equal(0, recorder.PostOrSendCount);
    }

    /// <summary>
    /// Records whether anything ever tried to marshal a continuation through this context, without
    /// throwing or blocking — a regression here fails the assertion cleanly instead of hanging or
    /// crashing the test run the way a context that deadlocks or throws would.
    /// </summary>
    private sealed class RecordingSynchronizationContext : SynchronizationContext
    {
        public int PostOrSendCount;

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref PostOrSendCount);
            d(state);
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref PostOrSendCount);
            d(state);
        }
    }

    /// <summary>
    /// Always rejects (so the same queued punch is retried every interval, guaranteeing a submit
    /// call — and therefore an await through PunchQueueService/EF and back — on every loop
    /// iteration) and counts how many times it was called, as an independent progress signal.
    /// </summary>
    private sealed class CountingRejectingBackendApiClient : IBackendApiClient
    {
        private readonly Action _onSubmit;

        public CountingRejectingBackendApiClient(Action onSubmit) => _onSubmit = onSubmit;

        public Task<EmployeeLookupResult?> LookupEmployeeAsync(string code, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<byte[]?> FetchTemplateAsync(Guid employeeId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<PunchBatchSubmitResult> SubmitPunchesAsync(IReadOnlyList<QueuedPunch> punches, CancellationToken ct = default)
        {
            _onSubmit();
            // TransientFailure (not RejectedByBackend): the queue must stay populated across every
            // interval so this keeps getting called — that's what proves the loop is still making
            // progress. RejectedByBackend would drop the punch after the first attempt, leaving
            // nothing left to retry on subsequent ticks.
            return Task.FromResult(PunchBatchSubmitResult.TransientFailure);
        }
    }
}
