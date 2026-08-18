using System.Collections.Concurrent;
using AttendanceAgent.Api;
using AttendanceAgent.Devices;
using AttendanceAgent.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AttendanceAgent.Tests;

/// <summary>
/// Regression coverage for the kiosk-UI freeze: <see cref="PunchCaptureService.CapturePunchAsync"/>
/// used to call the fully synchronous <see cref="DeviceCapture.CaptureOnce"/> directly, so the
/// device open + sensor poll + close ran on whatever thread invoked it. In the real app that is the
/// WPF dispatcher thread (MainViewModel.PunchAsync is a RelayCommand handler, and nothing in
/// agent/src uses ConfigureAwait(false), so the awaits before the capture also resume there).
/// Measured against a real ZK4500 that is ~1.3s for OpenDevice plus up to a 15s capture timeout —
/// ~16.5s of a completely frozen, non-repainting window per punch. The fix wraps the call in
/// Task.Run so the blocking work lands on a thread-pool thread instead.
///
/// A real WPF Dispatcher isn't available here, but the mechanism is reproducible: this test runs
/// CapturePunchAsync on a dedicated (non-thread-pool) thread carrying a pumping
/// SynchronizationContext standing in for the dispatcher. Because that context posts continuations
/// back to its own thread, every await inside CapturePunchAsync resumes there — exactly as it does
/// under WPF. Task.Run, by contrast, can only ever dispatch to the thread pool, and a
/// `new Thread(...)` is never a pool thread. So:
///
///   * after the fix, Capture() runs on a pool thread -> a different managed thread id;
///   * before the fix, CaptureOnce ran inline on the "dispatcher" thread -> the same id, and the
///     assertion below fails.
///
/// This also covers the "do the fakes have thread-affinity assumptions that break under Task.Run"
/// question: they don't, and the punch still succeeds end to end.
/// </summary>
public class PunchCaptureServiceThreadingTests
{
    [Fact]
    public void CapturePunch_RunsTheBlockingCapture_OffTheCallingThread()
    {
        using var db = TestDb.CreateInMemory();
        var api = new FakeBackendApiClient
        {
            LookupResult = new EmployeeLookupResult(Guid.NewGuid(), "E001", "Jane Doe"),
            TemplateResult = new byte[] { 1, 2, 3 },
        };
        var queue = new PunchQueueService(db);
        var service = new PunchCaptureService(
            new EmployeeDirectoryService(api, db, NullLogger<EmployeeDirectoryService>.Instance),
            new TemplateCacheService(api, db, NullLogger<TemplateCacheService>.Instance),
            new FakeFingerprintVerifier { AlwaysMatches = true },
            queue);

        var device = new ThreadRecordingFingerprintDevice();
        var dispatcherThreadId = 0;
        PunchResult? result = null;
        Exception? failure = null;

        var dispatcherThread = new Thread(() =>
        {
            try
            {
                var context = new PumpingSynchronizationContext();
                SynchronizationContext.SetSynchronizationContext(context);
                dispatcherThreadId = Environment.CurrentManagedThreadId;

                var task = service.CapturePunchAsync("E001", "In", device);
                context.PumpUntilCompleted(task, TimeSpan.FromSeconds(30));
                result = task.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        dispatcherThread.Start();
        Assert.True(dispatcherThread.Join(TimeSpan.FromSeconds(60)), "The punch never completed.");

        Assert.Null(failure);
        Assert.NotNull(result);
        Assert.True(result!.Success, result.Message);

        // The capture genuinely ran, and it did NOT run on the "dispatcher" thread.
        Assert.NotEqual(0, device.CaptureThreadId);
        Assert.NotEqual(dispatcherThreadId, device.CaptureThreadId);
        Assert.True(
            device.CaptureRanOnThreadPoolThread,
            "Expected the blocking capture to be dispatched to the thread pool via Task.Run.");
    }

    private sealed class ThreadRecordingFingerprintDevice : IFingerprintDevice
    {
        public int CaptureThreadId { get; private set; }
        public bool CaptureRanOnThreadPoolThread { get; private set; }

        public void Acquire() { }

        public byte[] Capture()
        {
            CaptureThreadId = Environment.CurrentManagedThreadId;
            CaptureRanOnThreadPoolThread = Thread.CurrentThread.IsThreadPoolThread;
            // Stand in for the real device's blocking work (OpenDevice + sensor poll)
            // so the calling thread would visibly be held if the fix regressed.
            Thread.Sleep(50);
            return new byte[] { 9, 9 };
        }

        public void Release() { }
    }

    /// <summary>
    /// Minimal single-threaded SynchronizationContext standing in for WPF's dispatcher: Post queues
    /// work back to the thread that owns the context, and <see cref="PumpUntilCompleted"/> drains
    /// that queue the way a message loop would. Bounded by a timeout and a polling TryTake so a
    /// regression fails or times out cleanly instead of hanging the test run forever.
    /// </summary>
    private sealed class PumpingSynchronizationContext : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

        public override void Send(SendOrPostCallback d, object? state) => d(state);

        public void PumpUntilCompleted(Task task, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (!task.IsCompleted && DateTime.UtcNow < deadline)
            {
                if (_queue.TryTake(out var work, TimeSpan.FromMilliseconds(20)))
                    work.Callback(work.State);
            }

            // Drain anything posted between the last check and completion.
            while (_queue.TryTake(out var trailing, TimeSpan.Zero))
                trailing.Callback(trailing.State);
        }
    }
}
