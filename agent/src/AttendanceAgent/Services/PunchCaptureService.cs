using AttendanceAgent.Devices;

namespace AttendanceAgent.Services;

public class PunchCaptureService : IPunchCaptureService
{
    private readonly IEmployeeDirectoryService _employees;
    private readonly ITemplateCacheService _templates;
    private readonly IFingerprintVerifier _verifier;
    private readonly IPunchQueueService _queue;

    public PunchCaptureService(
        IEmployeeDirectoryService employees,
        ITemplateCacheService templates,
        IFingerprintVerifier verifier,
        IPunchQueueService queue)
    {
        _employees = employees;
        _templates = templates;
        _verifier = verifier;
        _queue = queue;
    }

    public async Task<PunchResult> CapturePunchAsync(string employeeCode, string punchType, IFingerprintDevice device, CancellationToken ct = default)
    {
        var employee = await _employees.ResolveAsync(employeeCode, ct);
        if (employee is null)
            return new PunchResult(false, $"Employee code '{employeeCode}' not recognized.");

        var enrolledTemplate = await _templates.GetTemplateAsync(employee.EmployeeId, ct);
        if (enrolledTemplate is null)
            return new PunchResult(false, "No enrolled fingerprint template available (and none cached offline).");

        byte[] captured;
        try
        {
            captured = DeviceCapture.CaptureOnce(device);
        }
        catch (Exception ex)
        {
            return new PunchResult(false, $"Fingerprint capture failed: {ex.Message}");
        }

        if (!_verifier.Verify(captured, enrolledTemplate))
            return new PunchResult(false, "Fingerprint did not match.");

        await _queue.EnqueueAsync(employee.EmployeeId, punchType, DateTimeOffset.UtcNow, ct);
        return new PunchResult(true, $"Punch recorded for {employee.Name}.");
    }
}
