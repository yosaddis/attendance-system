using AttendanceAgent.Devices;

namespace AttendanceAgent.Services;

public class EnrollmentService : IEnrollmentService
{
    private const int RequiredCaptureCount = 3;

    private readonly IEmployeeDirectoryService _employees;
    private readonly Api.IBackendApiClient _api;
    private readonly ITemplateCacheService _templates;

    public EnrollmentService(IEmployeeDirectoryService employees, Api.IBackendApiClient api, ITemplateCacheService templates)
    {
        _employees = employees;
        _api = api;
        _templates = templates;
    }

    public async Task<EnrollmentResult> EnrollAsync(
        string employeeCode,
        IFingerprintDevice device,
        IFingerprintEnroller enroller,
        Action<int, int> onCaptureProgress,
        CancellationToken ct = default)
    {
        Api.EmployeeLookupResult? employee;
        try
        {
            employee = await _employees.ResolveAsync(employeeCode, ct);
        }
        catch (OperationCanceledException)
        {
            return new EnrollmentResult(false, "Enrollment cancelled.");
        }
        catch (Exception ex)
        {
            return new EnrollmentResult(false, $"Failed to look up employee code '{employeeCode}': {ex.Message}");
        }

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
        catch (OperationCanceledException)
        {
            return new EnrollmentResult(false, "Enrollment cancelled.");
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
            //
            // Wrapped in its own try/catch because an exception thrown from a finally block
            // REPLACES whatever the try/catch above was about to return — a capture failure
            // ("Fingerprint capture failed: sensor timeout") would otherwise be silently
            // discarded and replaced by a release error instead.
            try
            {
                await Task.Run(() => device.Release(), CancellationToken.None);
            }
            catch
            {
                // The try/catch above already produced the real result for this method to
                // return; a failure releasing the device doesn't change that verdict.
            }
        }

        byte[] merged;
        try
        {
            merged = await Task.Run(() => enroller.MergeCaptures(rawCaptures), ct);
        }
        catch (OperationCanceledException)
        {
            return new EnrollmentResult(false, "Enrollment cancelled.");
        }
        catch (Exception ex)
        {
            return new EnrollmentResult(false, $"Failed to build enrollment template: {ex.Message}");
        }

        // A Debug build's FakeFingerprintEnroller defaults MergedResult to Array.Empty<byte>() —
        // without this check, pointing a Debug build at a shared dev/staging backend and clicking
        // through the enrollment UI would upload a genuinely empty template and destructively
        // overwrite whatever real template that employee already had (the backend upserts with
        // no size floor). A real vendor enroller should never legitimately produce an empty
        // result either.
        if (merged.Length == 0)
            return new EnrollmentResult(false, "Enrollment produced an empty template — not uploading.");

        bool uploaded;
        try
        {
            uploaded = await _api.EnrollTemplateAsync(employee.EmployeeId, merged, ct);
        }
        catch (OperationCanceledException)
        {
            return new EnrollmentResult(false, "Enrollment cancelled.");
        }
        catch (Exception ex)
        {
            return new EnrollmentResult(false, $"Failed to upload the enrolled template to the backend: {ex.Message}");
        }

        if (!uploaded)
            return new EnrollmentResult(false, "Failed to upload the enrolled template to the backend.");

        // Without this, a just-enrolled employee can't punch until one successful ONLINE punch
        // populates TemplateCacheService's local cache (TemplateCacheService.GetTemplateAsync
        // only caches on a successful FETCH, not on enrollment) — surprising on a kiosk built
        // around offline tolerance for punches. Caching immediately here closes that gap.
        //
        // The upload above already succeeded — the enrollment IS complete server-side — so
        // NOTHING that can go wrong here (a locked agent.db, a full disk, or even a Cancel
        // click landing at this exact moment) should be reported as an enrollment failure; that
        // would send the operator to re-enroll a finger that's already correctly stored.
        // Swallow every outcome, including cancellation — the employee will still punch
        // successfully the moment an online punch (or the next sync) repopulates the cache.
        try
        {
            await _templates.CacheTemplateAsync(employee.EmployeeId, merged, ct);
        }
        catch
        {
            // Logged nowhere yet — EnrollmentService has no ILogger today. Not adding one just
            // for this: the upload already succeeded, so there is nothing actionable to report
            // beyond what a future ILogger addition to this class would carry.
        }

        return new EnrollmentResult(true, $"Fingerprint enrolled for {employee.Name}.");
    }
}
