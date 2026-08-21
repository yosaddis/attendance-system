using AttendanceAgent.Api;
using AttendanceAgent.Devices;
using AttendanceAgent.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AttendanceAgent.Tests;

/// <summary>
/// FakeFingerprintDevice's single NextEnrollmentCapture property can't return a different byte
/// array per call, but EnrollAsync_ThreeSuccessfulCaptures_MergesAndUploads needs each of the 3
/// captures distinguishable to prove ordering is preserved into MergeCaptures.
/// </summary>
public class SequencedFakeFingerprintDevice : IFingerprintDevice
{
    private readonly Queue<byte[]> _captures;
    public bool IsAcquired { get; private set; }

    public SequencedFakeFingerprintDevice(Queue<byte[]> captures) => _captures = captures;

    public void Acquire() => IsAcquired = true;
    public byte[] Capture() => throw new NotSupportedException("Not used by enrollment.");
    public byte[] CaptureForEnrollment() => _captures.Dequeue();
    public void Release() => IsAcquired = false;
}

/// <summary>
/// Cancels the enrollment's own CancellationTokenSource from INSIDE the first
/// CaptureForEnrollment() call, so the capture loop is genuinely mid-flight (device acquired) when
/// the token trips — the only state in which the finally-block Release() bug is observable.
/// </summary>
public class SelfCancellingFakeFingerprintDevice : IFingerprintDevice
{
    private readonly CancellationTokenSource _cts;
    public bool IsAcquired { get; private set; }
    public List<string> CallLog { get; } = new();

    public SelfCancellingFakeFingerprintDevice(CancellationTokenSource cts) => _cts = cts;

    public void Acquire()
    {
        IsAcquired = true;
        CallLog.Add("Acquire");
    }

    public byte[] Capture() => throw new NotSupportedException("Not used by enrollment.");

    public byte[] CaptureForEnrollment()
    {
        CallLog.Add("CaptureForEnrollment");
        _cts.Cancel();
        return new byte[] { 1 };
    }

    public void Release()
    {
        IsAcquired = false;
        CallLog.Add("Release");
    }
}

public class EnrollmentServiceTests
{
    [Fact]
    public async Task EnrollAsync_ThreeSuccessfulCaptures_MergesAndUploads()
    {
        using var db = TestDb.CreateInMemory();
        var employeeId = Guid.NewGuid();
        var api = new FakeBackendApiClient { LookupResult = new EmployeeLookupResult(employeeId, "E002", "Yoseph Addisu Abate") };
        var employees = new EmployeeDirectoryService(api, db, NullLogger<EmployeeDirectoryService>.Instance);
        var templates = new TemplateCacheService(api, db, NullLogger<TemplateCacheService>.Instance);
        var service = new EnrollmentService(employees, api, templates);
        var captureQueue = new Queue<byte[]>(new[] { new byte[] { 1 }, new byte[] { 2 }, new byte[] { 3 } });
        var enroller = new FakeFingerprintEnroller { MergedResult = new byte[] { 9, 9, 9 } };
        var progressCalls = new List<(int, int)>();

        var result = await service.EnrollAsync("E002", new SequencedFakeFingerprintDevice(captureQueue), enroller,
            (i, total) => progressCalls.Add((i, total)));

        Assert.True(result.Success);
        Assert.Equal(new[] { (1, 3), (2, 3), (3, 3) }, progressCalls);
        Assert.Equal(3, enroller.LastCaptures!.Count);
        Assert.Equal(new byte[] { 1 }, enroller.LastCaptures![0]);
        Assert.Equal(new byte[] { 2 }, enroller.LastCaptures![1]);
        Assert.Equal(new byte[] { 3 }, enroller.LastCaptures![2]);
        Assert.Equal(employeeId, api.LastEnrolledTemplate!.Value.EmployeeId);
        Assert.Equal(new byte[] { 9, 9, 9 }, api.LastEnrolledTemplate!.Value.TemplateData);

        // Without this, a just-enrolled employee can't punch until one successful ONLINE punch
        // separately populates the cache — confirm EnrollAsync itself already did it, not just
        // that the upload succeeded.
        var cached = await db.CachedTemplates.FindAsync(employeeId);
        Assert.NotNull(cached);
        Assert.Equal(new byte[] { 9, 9, 9 }, cached!.TemplateData);
    }

    [Fact]
    public async Task EnrollAsync_UnknownEmployeeCode_FailsWithoutTouchingDevice()
    {
        using var db = TestDb.CreateInMemory();
        var api = new FakeBackendApiClient();
        var employees = new EmployeeDirectoryService(api, db, NullLogger<EmployeeDirectoryService>.Instance);
        var templates = new TemplateCacheService(api, db, NullLogger<TemplateCacheService>.Instance);
        var service = new EnrollmentService(employees, api, templates);
        var device = new FakeFingerprintDevice();

        var result = await service.EnrollAsync("NOPE", device, new FakeFingerprintEnroller(), (_, _) => { });

        Assert.False(result.Success);
        Assert.False(device.IsAcquired);
        Assert.Empty(device.CallLog);
        Assert.Null(api.LastEnrolledTemplate);
    }

    [Fact]
    public async Task EnrollAsync_CaptureFails_AbortsAndReleasesDevice()
    {
        using var db = TestDb.CreateInMemory();
        var employeeId = Guid.NewGuid();
        var api = new FakeBackendApiClient { LookupResult = new EmployeeLookupResult(employeeId, "E002", "Yoseph Addisu Abate") };
        var employees = new EmployeeDirectoryService(api, db, NullLogger<EmployeeDirectoryService>.Instance);
        var templates = new TemplateCacheService(api, db, NullLogger<TemplateCacheService>.Instance);
        var service = new EnrollmentService(employees, api, templates);
        var device = new FakeFingerprintDevice { ThrowOnCaptureForEnrollment = true };

        var result = await service.EnrollAsync("E002", device, new FakeFingerprintEnroller(), (_, _) => { });

        Assert.False(result.Success);
        Assert.False(device.IsAcquired);
        Assert.Null(api.LastEnrolledTemplate);
    }

    [Fact]
    public async Task EnrollAsync_MergeFails_DoesNotUpload()
    {
        using var db = TestDb.CreateInMemory();
        var employeeId = Guid.NewGuid();
        var api = new FakeBackendApiClient { LookupResult = new EmployeeLookupResult(employeeId, "E002", "Yoseph Addisu Abate") };
        var employees = new EmployeeDirectoryService(api, db, NullLogger<EmployeeDirectoryService>.Instance);
        var templates = new TemplateCacheService(api, db, NullLogger<TemplateCacheService>.Instance);
        var service = new EnrollmentService(employees, api, templates);
        var device = new FakeFingerprintDevice { NextEnrollmentCapture = new byte[] { 1 } };
        var enroller = new FakeFingerprintEnroller { ThrowOnMerge = true };

        var result = await service.EnrollAsync("E002", device, enroller, (_, _) => { });

        Assert.False(result.Success);
        Assert.Null(api.LastEnrolledTemplate);
    }

    [Fact]
    public async Task EnrollAsync_UploadFails_ReturnsFailureResult()
    {
        using var db = TestDb.CreateInMemory();
        var employeeId = Guid.NewGuid();
        var api = new FakeBackendApiClient
        {
            LookupResult = new EmployeeLookupResult(employeeId, "E002", "Yoseph Addisu Abate"),
            EnrollTemplateResult = false,
        };
        var employees = new EmployeeDirectoryService(api, db, NullLogger<EmployeeDirectoryService>.Instance);
        var templates = new TemplateCacheService(api, db, NullLogger<TemplateCacheService>.Instance);
        var service = new EnrollmentService(employees, api, templates);
        var device = new FakeFingerprintDevice { NextEnrollmentCapture = new byte[] { 1 } };

        var result = await service.EnrollAsync("E002", device, new FakeFingerprintEnroller(), (_, _) => { });

        Assert.False(result.Success);
    }

    [Fact]
    public async Task EnrollAsync_EmptyMergedTemplate_FailsWithoutUploading()
    {
        using var db = TestDb.CreateInMemory();
        var employeeId = Guid.NewGuid();
        var api = new FakeBackendApiClient { LookupResult = new EmployeeLookupResult(employeeId, "E002", "Yoseph Addisu Abate") };
        var employees = new EmployeeDirectoryService(api, db, NullLogger<EmployeeDirectoryService>.Instance);
        var templates = new TemplateCacheService(api, db, NullLogger<TemplateCacheService>.Instance);
        var service = new EnrollmentService(employees, api, templates);
        var device = new FakeFingerprintDevice { NextEnrollmentCapture = new byte[] { 1 } };
        var enroller = new FakeFingerprintEnroller { MergedResult = Array.Empty<byte>() };

        var result = await service.EnrollAsync("E002", device, enroller, (_, _) => { });

        Assert.False(result.Success);
        Assert.Null(api.LastEnrolledTemplate);
    }

    [Fact]
    public async Task EnrollAsync_UploadFailure_ReturnsFailureMessage_CoveringTheGapThatLetC1Survive()
    {
        using var db = TestDb.CreateInMemory();
        var employeeId = Guid.NewGuid();
        var api = new FakeBackendApiClient
        {
            LookupResult = new EmployeeLookupResult(employeeId, "E002", "Yoseph Addisu Abate"),
            ThrowOnEnrollTemplate = true,
        };
        var employees = new EmployeeDirectoryService(api, db, NullLogger<EmployeeDirectoryService>.Instance);
        var templates = new TemplateCacheService(api, db, NullLogger<TemplateCacheService>.Instance);
        var service = new EnrollmentService(employees, api, templates);
        var device = new FakeFingerprintDevice { NextEnrollmentCapture = new byte[] { 1 } };

        var result = await service.EnrollAsync("E002", device, new FakeFingerprintEnroller(), (_, _) => { });

        Assert.False(result.Success);
    }

    [Fact]
    public async Task EnrollAsync_ReleaseRunsEvenWhenCancellationTokenIsAlreadyCancelled()
    {
        // The bug this proves is fixed: `finally { await Task.Run(() => device.Release(), ct); }`
        // using the SAME ct that just cancelled the capture loop means Task.Run would skip invoking
        // the delegate entirely for an already-cancelled token — Release() silently never runs.
        //
        // Cancellation is triggered from inside the FIRST CaptureForEnrollment() (rather than
        // pre-cancelling the token before the call) so the device is genuinely acquired when the
        // token trips: a token cancelled before EnrollAsync is even entered never reaches the
        // Acquire()/Release() pair at all, so it cannot observe this bug.
        using var db = TestDb.CreateInMemory();
        var employeeId = Guid.NewGuid();
        var api = new FakeBackendApiClient { LookupResult = new EmployeeLookupResult(employeeId, "E002", "Yoseph Addisu Abate") };
        var employees = new EmployeeDirectoryService(api, db, NullLogger<EmployeeDirectoryService>.Instance);
        var templates = new TemplateCacheService(api, db, NullLogger<TemplateCacheService>.Instance);
        var service = new EnrollmentService(employees, api, templates);
        using var cts = new CancellationTokenSource();
        var device = new SelfCancellingFakeFingerprintDevice(cts);

        var result = await service.EnrollAsync("E002", device, new FakeFingerprintEnroller(), (_, _) => { }, cts.Token);

        Assert.False(result.Success);
        Assert.Equal("Enrollment cancelled.", result.Message);
        Assert.False(device.IsAcquired);
        Assert.Contains("Release", device.CallLog);

        // A cancelled enrollment must not upload anything — the whole point of Cancel.
        Assert.Null(api.LastEnrolledTemplate);
    }

    [Fact]
    public async Task EnrollAsync_CancelledBeforeEmployeeLookupCompletes_ReturnsCancelledMessage_DoesNotEscape()
    {
        // A whole-branch re-review found _employees.ResolveAsync(employeeCode, ct) was the only
        // unguarded collaborator call in this method: EmployeeDirectoryService.ResolveAsync's own
        // catch deliberately re-throws on cancellation (catch (Exception ex) when
        // (!ct.IsCancellationRequested)), and nothing here caught it — an
        // OperationCanceledException from the cache-upsert EF query would have escaped all the way
        // to the WPF dispatcher, exactly like the original C1 crash.
        using var db = TestDb.CreateInMemory();
        var employeeId = Guid.NewGuid();
        var api = new FakeBackendApiClient { LookupResult = new EmployeeLookupResult(employeeId, "E002", "Yoseph Addisu Abate") };
        var employees = new EmployeeDirectoryService(api, db, NullLogger<EmployeeDirectoryService>.Instance);
        var templates = new TemplateCacheService(api, db, NullLogger<TemplateCacheService>.Instance);
        var service = new EnrollmentService(employees, api, templates);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await service.EnrollAsync("E002", new FakeFingerprintDevice(), new FakeFingerprintEnroller(), (_, _) => { }, cts.Token);

        Assert.False(result.Success);
        Assert.Equal("Enrollment cancelled.", result.Message);
    }

    [Fact]
    public async Task EnrollAsync_TemplateCacheThrowsAfterSuccessfulUpload_StillReturnsSuccess()
    {
        // The upload already succeeded server-side by this point — a failure populating the
        // LOCAL cache (a locked agent.db, a full disk) must not be reported as an enrollment
        // failure, or the operator would re-enroll a finger that's already correctly stored.
        using var db = TestDb.CreateInMemory();
        var employeeId = Guid.NewGuid();
        var api = new FakeBackendApiClient { LookupResult = new EmployeeLookupResult(employeeId, "E002", "Yoseph Addisu Abate") };
        var employees = new EmployeeDirectoryService(api, db, NullLogger<EmployeeDirectoryService>.Instance);
        var service = new EnrollmentService(employees, api, new ThrowingTemplateCacheService());
        var device = new FakeFingerprintDevice { NextEnrollmentCapture = new byte[] { 1 } };
        var enroller = new FakeFingerprintEnroller { MergedResult = new byte[] { 9, 9, 9 } };

        var result = await service.EnrollAsync("E002", device, enroller, (_, _) => { });

        Assert.True(result.Success);
        Assert.Equal(employeeId, api.LastEnrolledTemplate!.Value.EmployeeId);
    }

    [Fact]
    public async Task EnrollAsync_ReleaseThrows_DoesNotMaskTheCaptureFailureMessage()
    {
        // An exception thrown from a finally block REPLACES whatever the try/catch above it was
        // about to return — without its own try/catch, a capture failure ("Fingerprint capture
        // failed: ...") would be silently discarded and replaced by a release error instead.
        using var db = TestDb.CreateInMemory();
        var employeeId = Guid.NewGuid();
        var api = new FakeBackendApiClient { LookupResult = new EmployeeLookupResult(employeeId, "E002", "Yoseph Addisu Abate") };
        var employees = new EmployeeDirectoryService(api, db, NullLogger<EmployeeDirectoryService>.Instance);
        var templates = new TemplateCacheService(api, db, NullLogger<TemplateCacheService>.Instance);
        var service = new EnrollmentService(employees, api, templates);
        var device = new ThrowOnReleaseFakeFingerprintDevice();

        var result = await service.EnrollAsync("E002", device, new FakeFingerprintEnroller(), (_, _) => { });

        Assert.False(result.Success);
        Assert.Contains("Fingerprint capture failed", result.Message);
    }
}

file sealed class ThrowingTemplateCacheService : ITemplateCacheService
{
    public Task<byte[]?> GetTemplateAsync(Guid employeeId, CancellationToken ct = default) =>
        throw new NotSupportedException("Not used by this test.");

    public Task CacheTemplateAsync(Guid employeeId, byte[] templateData, CancellationToken ct = default) =>
        throw new InvalidOperationException("Simulated local cache failure (e.g. a locked agent.db).");
}

file sealed class ThrowOnReleaseFakeFingerprintDevice : IFingerprintDevice
{
    public void Acquire() { }
    public byte[] Capture() => throw new NotSupportedException("Not used by enrollment.");
    public byte[] CaptureForEnrollment() => throw new InvalidOperationException("No finger detected");
    public void Release() => throw new InvalidOperationException("Simulated release failure.");
}
