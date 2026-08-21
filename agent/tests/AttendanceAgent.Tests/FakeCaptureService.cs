using AttendanceAgent.Devices;
using AttendanceAgent.Services;

namespace AttendanceAgent.Tests;

public class FakeCaptureService : IPunchCaptureService
{
    public PunchResult Result { get; set; } = new(true, "ok");

    /// <summary>
    /// When set, CapturePunchAsync awaits this instead of returning Result immediately — lets a
    /// test hold "punch in progress" open to assert CanExecute states on other commands.
    /// </summary>
    public TaskCompletionSource<PunchResult>? PendingCompletion { get; set; }

    public bool ThrowOnCapture { get; set; }

    public async Task<PunchResult> CapturePunchAsync(string employeeCode, string punchType, IFingerprintDevice device, CancellationToken ct = default)
    {
        if (ThrowOnCapture) throw new InvalidOperationException("Simulated capture failure.");
        if (PendingCompletion is null) return Result;
        return await PendingCompletion.Task;
    }
}
