using AttendanceAgent.Devices;
using AttendanceAgent.Services;

namespace AttendanceAgent.Tests;

public class FakeCaptureService : IPunchCaptureService
{
    public PunchResult Result { get; set; } = new(true, "ok");

    public Task<PunchResult> CapturePunchAsync(string employeeCode, string punchType, IFingerprintDevice device, CancellationToken ct = default) =>
        Task.FromResult(Result);
}
