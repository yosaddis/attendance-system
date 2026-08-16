using AttendanceAgent.Api;
using AttendanceAgent.Devices;
using AttendanceAgent.Services;
using Xunit;

namespace AttendanceAgent.Tests;

public class PunchCaptureServiceTests
{
    [Fact]
    public async Task CapturePunch_MatchingFingerprint_EnqueuesPunch()
    {
        using var db = TestDb.CreateInMemory();
        var employeeId = Guid.NewGuid();
        var api = new FakeBackendApiClient
        {
            LookupResult = new EmployeeLookupResult(employeeId, "E001", "Jane Doe"),
            TemplateResult = new byte[] { 1, 2, 3 },
        };
        var queue = new PunchQueueService(db);
        var service = new PunchCaptureService(
            new EmployeeDirectoryService(api, db),
            new TemplateCacheService(api, db),
            new FakeFingerprintVerifier { AlwaysMatches = true },
            queue);
        var device = new FakeFingerprintDevice { NextCapture = new byte[] { 9, 9 } };

        var result = await service.CapturePunchAsync("E001", "In", device);

        Assert.True(result.Success);
        Assert.Single(await queue.GetPendingAsync());
        Assert.False(device.IsAcquired);
    }

    [Fact]
    public async Task CapturePunch_UnknownEmployee_FailsWithoutEnqueueing()
    {
        using var db = TestDb.CreateInMemory();
        var api = new FakeBackendApiClient();
        var queue = new PunchQueueService(db);
        var service = new PunchCaptureService(
            new EmployeeDirectoryService(api, db),
            new TemplateCacheService(api, db),
            new FakeFingerprintVerifier(),
            queue);

        var result = await service.CapturePunchAsync("NOPE", "In", new FakeFingerprintDevice());

        Assert.False(result.Success);
        Assert.Empty(await queue.GetPendingAsync());
    }

    [Fact]
    public async Task CapturePunch_FingerprintMismatch_DoesNotEnqueue()
    {
        using var db = TestDb.CreateInMemory();
        var employeeId = Guid.NewGuid();
        var api = new FakeBackendApiClient
        {
            LookupResult = new EmployeeLookupResult(employeeId, "E001", "Jane Doe"),
            TemplateResult = new byte[] { 1, 2, 3 },
        };
        var queue = new PunchQueueService(db);
        var service = new PunchCaptureService(
            new EmployeeDirectoryService(api, db),
            new TemplateCacheService(api, db),
            new FakeFingerprintVerifier { AlwaysMatches = false },
            queue);

        var result = await service.CapturePunchAsync("E001", "In", new FakeFingerprintDevice());

        Assert.False(result.Success);
        Assert.Empty(await queue.GetPendingAsync());
    }

    [Fact]
    public async Task CapturePunch_CaptureThrows_FailsWithoutEnqueueing()
    {
        using var db = TestDb.CreateInMemory();
        var employeeId = Guid.NewGuid();
        var api = new FakeBackendApiClient
        {
            LookupResult = new EmployeeLookupResult(employeeId, "E001", "Jane Doe"),
            TemplateResult = new byte[] { 1, 2, 3 },
        };
        var queue = new PunchQueueService(db);
        var service = new PunchCaptureService(
            new EmployeeDirectoryService(api, db),
            new TemplateCacheService(api, db),
            new FakeFingerprintVerifier { AlwaysMatches = true },
            queue);
        var device = new FakeFingerprintDevice { ThrowOnCapture = true };

        var result = await service.CapturePunchAsync("E001", "In", device);

        Assert.False(result.Success);
        Assert.Empty(await queue.GetPendingAsync());
        Assert.False(device.IsAcquired);
    }

    [Fact]
    public async Task CapturePunch_NoTemplateAvailable_Fails()
    {
        using var db = TestDb.CreateInMemory();
        var employeeId = Guid.NewGuid();
        var api = new FakeBackendApiClient
        {
            LookupResult = new EmployeeLookupResult(employeeId, "E001", "Jane Doe"),
            TemplateResult = null,
        };
        var queue = new PunchQueueService(db);
        var service = new PunchCaptureService(
            new EmployeeDirectoryService(api, db),
            new TemplateCacheService(api, db),
            new FakeFingerprintVerifier(),
            queue);

        var result = await service.CapturePunchAsync("E001", "In", new FakeFingerprintDevice());

        Assert.False(result.Success);
        Assert.Empty(await queue.GetPendingAsync());
    }
}
