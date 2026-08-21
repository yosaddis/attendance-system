using AttendanceAgent.Devices;
using AttendanceAgent.Services;

namespace AttendanceAgent.Tests;

public class FakeEnrollmentService : IEnrollmentService
{
    public EnrollmentResult Result { get; set; } = new(true, "ok");

    public Task<EnrollmentResult> EnrollAsync(
        string employeeCode,
        IFingerprintDevice device,
        IFingerprintEnroller enroller,
        Action<int, int> onCaptureProgress,
        CancellationToken ct = default) =>
        Task.FromResult(Result);
}
