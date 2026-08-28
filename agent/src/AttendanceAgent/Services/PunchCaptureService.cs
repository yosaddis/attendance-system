using AttendanceAgent.Api;
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

    private const int PunchWindowMarginMinutes = 30;

    public async Task<PunchResult> CapturePunchAsync(string employeeCode, string punchType, IFingerprintDevice device, CancellationToken ct = default)
    {
        var employee = await _employees.ResolveAsync(employeeCode, ct);
        if (employee is null)
            return new PunchResult(false, $"Employee code '{employeeCode}' not recognized.");

        var windowRejection = CheckPunchWindow(punchType, employee);
        if (windowRejection is not null)
            return new PunchResult(false, windowRejection);

        var enrolledTemplate = await _templates.GetTemplateAsync(employee.EmployeeId, ct);
        if (enrolledTemplate is null)
            return new PunchResult(false, "No enrolled fingerprint template available (and none cached offline).");

        byte[] captured;
        try
        {
            // DeviceCapture.CaptureOnce is fully synchronous and slow by nature: it
            // opens the device, polls the sensor until a finger arrives, and closes
            // it again. Measured against a real ZK4500, OpenDevice alone costs
            // ~1.3s and a punch where nobody presents a finger burns the whole 15s
            // capture timeout — ~16.5s total. Called directly, that ran on
            // whatever thread invoked CapturePunchAsync, which in the real app is
            // the WPF dispatcher thread (MainViewModel.PunchAsync is a RelayCommand
            // handler, and nothing in agent/src uses ConfigureAwait(false), so
            // continuations resume on the UI thread too). The kiosk window froze
            // solid for the duration — no repaint, no "place your finger" feedback,
            // reported by Windows as "Not Responding". Task.Run pushes the blocking
            // work onto a thread-pool thread so the dispatcher stays free to pump.
            //
            // This affects both vendors — SecuGenFingerprintDevice.Capture() blocks
            // the same way — which is why the fix lives here in the shared service
            // rather than in either device implementation.
            //
            // `ct` here only prevents the work from STARTING if cancellation has
            // already been requested; the native capture call itself is not
            // interruptible, so an in-flight capture still runs to its own timeout.
            captured = await Task.Run(() => DeviceCapture.CaptureOnce(device), ct);
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

    // Returns a rejection message if `now` falls outside the ±30 minute window around the shift
    // time matching this punch type, or null if the punch is allowed — including when the
    // employee has no shift, or the shift has no configured time for this punch type (e.g. no
    // BreakStart/BreakEnd on a 2-punch shift). Nothing to compare against means nothing to enforce.
    private static string? CheckPunchWindow(string punchType, EmployeeLookupResult employee)
    {
        var (shiftTime, punchLabel) = punchType switch
        {
            "In" => (employee.ShiftStartTime, "In"),
            "Out" => (employee.ShiftEndTime, "Out"),
            "BreakOut" => (employee.ShiftBreakStart, "Break Out"),
            "BreakIn" => (employee.ShiftBreakEnd, "Break In"),
            _ => ((TimeOnly?)null, punchType),
        };

        if (shiftTime is null) return null;

        // Signed shortest-path distance (in minutes) from the shift time to now, wrapping
        // correctly around midnight. Raw, unwrapped time-of-day arithmetic (comparing TimeSpans
        // directly) miscompares whenever a shift boundary sits within the margin of midnight and
        // "now" falls on the other side of it — e.g. shift ends 23:50, now is 00:10; the naive
        // difference looks like ~23h40m instead of the real 20 minutes.
        var now = TimeOnly.FromDateTime(DateTime.Now);
        var diffMinutes = (now.Hour * 60 + now.Minute) - (shiftTime.Value.Hour * 60 + shiftTime.Value.Minute);
        if (diffMinutes > 720) diffMinutes -= 1440;
        if (diffMinutes <= -720) diffMinutes += 1440;

        if (Math.Abs(diffMinutes) <= PunchWindowMarginMinutes) return null;

        var tooEarly = diffMinutes < 0;
        var windowStart = shiftTime.Value.AddMinutes(-PunchWindowMarginMinutes).ToString("h:mm tt");
        var windowEnd = shiftTime.Value.AddMinutes(PunchWindowMarginMinutes).ToString("h:mm tt");
        return $"Too {(tooEarly ? "early" : "late")} to punch {punchLabel} — accepted from {windowStart} to {windowEnd}.";
    }
}
