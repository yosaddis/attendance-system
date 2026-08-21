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
        var service = new EnrollmentService(employees, api);
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
    }

    [Fact]
    public async Task EnrollAsync_UnknownEmployeeCode_FailsWithoutTouchingDevice()
    {
        using var db = TestDb.CreateInMemory();
        var api = new FakeBackendApiClient();
        var employees = new EmployeeDirectoryService(api, db, NullLogger<EmployeeDirectoryService>.Instance);
        var service = new EnrollmentService(employees, api);
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
        var service = new EnrollmentService(employees, api);
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
        var service = new EnrollmentService(employees, api);
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
        var service = new EnrollmentService(employees, api);
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
        var service = new EnrollmentService(employees, api);
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
}
