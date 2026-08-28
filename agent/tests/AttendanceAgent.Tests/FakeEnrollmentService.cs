using AttendanceAgent.Devices;
using AttendanceAgent.Services;

namespace AttendanceAgent.Tests;

public class FakeEnrollmentService : IEnrollmentService
{
    public EnrollmentResult Result { get; set; } = new(true, "ok");

    /// <summary>
    /// When set, EnrollAsync awaits this instead of returning Result immediately — lets a test
    /// hold "enrollment in progress" open to assert CanExecute states, then either complete it
    /// normally or observe cancellation via LastCancellationToken.
    /// </summary>
    public TaskCompletionSource<EnrollmentResult>? PendingCompletion { get; set; }

    public CancellationToken? LastCancellationToken { get; private set; }

    public async Task<EnrollmentResult> EnrollAsync(
        string employeeCode,
        IFingerprintDevice device,
        IFingerprintEnroller enroller,
        Action<int, int> onCaptureProgress,
        CancellationToken ct = default)
    {
        LastCancellationToken = ct;
        if (PendingCompletion is null) return Result;

        using var registration = ct.Register(() =>
            PendingCompletion.TrySetResult(new EnrollmentResult(false, "Enrollment cancelled.")));
        return await PendingCompletion.Task;
    }
}
