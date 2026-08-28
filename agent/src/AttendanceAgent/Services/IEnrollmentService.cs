using AttendanceAgent.Devices;

namespace AttendanceAgent.Services;

public record EnrollmentResult(bool Success, string Message);

public interface IEnrollmentService
{
    Task<EnrollmentResult> EnrollAsync(
        string employeeCode,
        IFingerprintDevice device,
        IFingerprintEnroller enroller,
        Action<int, int> onCaptureProgress,
        CancellationToken ct = default);
}
