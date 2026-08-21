using AttendanceAgent.Devices;

namespace AttendanceAgent.Services;

public class EnrollmentService : IEnrollmentService
{
    private const int RequiredCaptureCount = 3;

    private readonly IEmployeeDirectoryService _employees;
    private readonly Api.IBackendApiClient _api;

    public EnrollmentService(IEmployeeDirectoryService employees, Api.IBackendApiClient api)
    {
        _employees = employees;
        _api = api;
    }

    public async Task<EnrollmentResult> EnrollAsync(
        string employeeCode,
        IFingerprintDevice device,
        IFingerprintEnroller enroller,
        Action<int, int> onCaptureProgress,
        CancellationToken ct = default)
    {
        var employee = await _employees.ResolveAsync(employeeCode, ct);
        if (employee is null)
            return new EnrollmentResult(false, $"Employee code '{employeeCode}' not recognized.");

        // Mirrors DeviceCapture.CaptureOnce's safety contract: Acquire() runs OUTSIDE the
        // try/finally below, so a failed Acquire() (which some device implementations already
        // guarantee cleans up its own partial state before throwing — see
        // ZkFingerprintDevice.Acquire()) never triggers a Release() call on a device that was
        // never successfully opened.
        try
        {
            await Task.Run(() => device.Acquire(), ct);
        }
        catch (Exception ex)
        {
            return new EnrollmentResult(false, $"Failed to access fingerprint device: {ex.Message}");
        }

        var rawCaptures = new List<byte[]>();
        try
        {
            for (var i = 1; i <= RequiredCaptureCount; i++)
            {
                // Runs on the calling context (the WPF dispatcher, when called from
                // MainViewModel) because nothing in this method uses ConfigureAwait(false) —
                // each `await Task.Run(...)` below hops onto a thread-pool thread only for the
                // blocking device call itself, then resumes back here, so it's safe for the
                // caller to update UI-bound state directly inside onCaptureProgress.
                onCaptureProgress(i, RequiredCaptureCount);
                var capture = await Task.Run(() => device.CaptureForEnrollment(), ct);
                rawCaptures.Add(capture);
            }
        }
        catch (OperationCanceledException)
        {
            return new EnrollmentResult(false, "Enrollment cancelled.");
        }
        catch (Exception ex)
        {
            return new EnrollmentResult(false, $"Fingerprint capture failed: {ex.Message}");
        }
        finally
        {
            // Deliberately CancellationToken.None, NOT ct: this cleanup must run even when ct is
            // already cancelled. Task.Run(delegate, ct) with an already-cancelled token returns a
            // cancelled Task WITHOUT EVER INVOKING the delegate — so passing ct here (as this
            // method originally did) meant a cancelled enrollment could skip calling Release()
            // entirely, leaving the device open with the handle still held. Cleanup is not
            // cancellable.
            await Task.Run(() => device.Release(), CancellationToken.None);
        }

        byte[] merged;
        try
        {
            merged = await Task.Run(() => enroller.MergeCaptures(rawCaptures), ct);
        }
        catch (Exception ex)
        {
            return new EnrollmentResult(false, $"Failed to build enrollment template: {ex.Message}");
        }

        var uploaded = await _api.EnrollTemplateAsync(employee.EmployeeId, merged, ct);
        return uploaded
            ? new EnrollmentResult(true, $"Fingerprint enrolled for {employee.Name}.")
            : new EnrollmentResult(false, "Failed to upload the enrolled template to the backend.");
    }
}
