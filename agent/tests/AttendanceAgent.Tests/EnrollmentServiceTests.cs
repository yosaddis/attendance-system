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
}
