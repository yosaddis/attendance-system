using AttendanceAgent.Devices;

namespace AttendanceAgent.Services;

public record PunchResult(bool Success, string Message);

public interface IPunchCaptureService
{
    Task<PunchResult> CapturePunchAsync(string employeeCode, string punchType, IFingerprintDevice device, CancellationToken ct = default);
}
