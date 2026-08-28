using AttendanceAgent.Api;
using AttendanceAgent.Devices;
using AttendanceAgent.Services;
using Microsoft.Extensions.Logging.Abstractions;
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
            new EmployeeDirectoryService(api, db, NullLogger<EmployeeDirectoryService>.Instance),
            new TemplateCacheService(api, db, NullLogger<TemplateCacheService>.Instance),
            new FakeFingerprintVerifier { AlwaysMatches = true },
            queue);
        var device = new FakeFingerprintDevice { NextCapture = new byte[] { 9, 9 } };

        var result = await service.CapturePunchAsync("E001", "In", device);

        Assert.True(result.Success);
        Assert.Single(await queue.GetPendingAsync());
        Assert.False(device.IsAcquired);
    }

    [Fact]
    public async Task CapturePunch_WithinShiftStartWindow_Succeeds()
    {
        using var db = TestDb.CreateInMemory();
        var employeeId = Guid.NewGuid();
        var shiftStart = TimeOnly.FromDateTime(DateTime.Now.AddMinutes(-20));
        var api = new FakeBackendApiClient
        {
            LookupResult = new EmployeeLookupResult(employeeId, "E001", "Jane Doe", ShiftStartTime: shiftStart),
            TemplateResult = new byte[] { 1, 2, 3 },
        };
        var queue = new PunchQueueService(db);
        var service = new PunchCaptureService(
            new EmployeeDirectoryService(api, db, NullLogger<EmployeeDirectoryService>.Instance),
            new TemplateCacheService(api, db, NullLogger<TemplateCacheService>.Instance),
            new FakeFingerprintVerifier { AlwaysMatches = true },
            queue);
        var device = new FakeFingerprintDevice { NextCapture = new byte[] { 9, 9 } };

        var result = await service.CapturePunchAsync("E001", "In", device);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task CapturePunch_BeforeShiftStartWindow_FailsWithoutTouchingDevice()
    {
        using var db = TestDb.CreateInMemory();
        var employeeId = Guid.NewGuid();
        var shiftStart = TimeOnly.FromDateTime(DateTime.Now.AddMinutes(45));
        var api = new FakeBackendApiClient
        {
            LookupResult = new EmployeeLookupResult(employeeId, "E001", "Jane Doe", ShiftStartTime: shiftStart),
            TemplateResult = new byte[] { 1, 2, 3 },
        };
        var queue = new PunchQueueService(db);
        var service = new PunchCaptureService(
            new EmployeeDirectoryService(api, db, NullLogger<EmployeeDirectoryService>.Instance),
            new TemplateCacheService(api, db, NullLogger<TemplateCacheService>.Instance),
            new FakeFingerprintVerifier { AlwaysMatches = true },
            queue);
        var device = new FakeFingerprintDevice { NextCapture = new byte[] { 9, 9 } };

        var result = await service.CapturePunchAsync("E001", "In", device);

        Assert.False(result.Success);
        Assert.Contains("Too early", result.Message);
        Assert.False(device.IsAcquired);
        Assert.Empty(await queue.GetPendingAsync());
    }

    [Fact]
    public async Task CapturePunch_AfterShiftEndWindow_FailsWithoutTouchingDevice()
    {
        using var db = TestDb.CreateInMemory();
        var employeeId = Guid.NewGuid();
        var shiftEnd = TimeOnly.FromDateTime(DateTime.Now.AddMinutes(-45));
        var api = new FakeBackendApiClient
        {
            LookupResult = new EmployeeLookupResult(employeeId, "E001", "Jane Doe", ShiftEndTime: shiftEnd),
            TemplateResult = new byte[] { 1, 2, 3 },
        };
        var queue = new PunchQueueService(db);
        var service = new PunchCaptureService(
            new EmployeeDirectoryService(api, db, NullLogger<EmployeeDirectoryService>.Instance),
            new TemplateCacheService(api, db, NullLogger<TemplateCacheService>.Instance),
            new FakeFingerprintVerifier { AlwaysMatches = true },
            queue);
        var device = new FakeFingerprintDevice { NextCapture = new byte[] { 9, 9 } };

        var result = await service.CapturePunchAsync("E001", "Out", device);

        Assert.False(result.Success);
        Assert.Contains("Too late", result.Message);
        Assert.False(device.IsAcquired);
        Assert.Empty(await queue.GetPendingAsync());
    }

    [Fact]
    public async Task CapturePunch_BreakPunchWithNoConfiguredBreakTimes_IsUnrestricted()
    {
        using var db = TestDb.CreateInMemory();
        var employeeId = Guid.NewGuid();
        var api = new FakeBackendApiClient
        {
            LookupResult = new EmployeeLookupResult(
                employeeId, "E001", "Jane Doe", ShiftStartTime: new TimeOnly(9, 0), ShiftEndTime: new TimeOnly(17, 0)),
            TemplateResult = new byte[] { 1, 2, 3 },
        };
        var queue = new PunchQueueService(db);
        var service = new PunchCaptureService(
            new EmployeeDirectoryService(api, db, NullLogger<EmployeeDirectoryService>.Instance),
            new TemplateCacheService(api, db, NullLogger<TemplateCacheService>.Instance),
            new FakeFingerprintVerifier { AlwaysMatches = true },
            queue);
        var device = new FakeFingerprintDevice { NextCapture = new byte[] { 9, 9 } };

        var result = await service.CapturePunchAsync("E001", "BreakOut", device);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task CapturePunch_BreakOutOutsideConfiguredBreakWindow_FailsWithoutTouchingDevice()
    {
        using var db = TestDb.CreateInMemory();
        var employeeId = Guid.NewGuid();
        var breakStart = TimeOnly.FromDateTime(DateTime.Now.AddMinutes(45));
        var api = new FakeBackendApiClient
        {
            LookupResult = new EmployeeLookupResult(employeeId, "E001", "Jane Doe", ShiftBreakStart: breakStart),
            TemplateResult = new byte[] { 1, 2, 3 },
        };
        var queue = new PunchQueueService(db);
        var service = new PunchCaptureService(
            new EmployeeDirectoryService(api, db, NullLogger<EmployeeDirectoryService>.Instance),
            new TemplateCacheService(api, db, NullLogger<TemplateCacheService>.Instance),
            new FakeFingerprintVerifier { AlwaysMatches = true },
            queue);
        var device = new FakeFingerprintDevice { NextCapture = new byte[] { 9, 9 } };

        var result = await service.CapturePunchAsync("E001", "BreakOut", device);

        Assert.False(result.Success);
        Assert.Contains("Too early", result.Message);
        Assert.Contains("Break Out", result.Message);
        Assert.False(device.IsAcquired);
        Assert.Empty(await queue.GetPendingAsync());
    }

    [Fact]
    public async Task CapturePunch_BreakInOutsideConfiguredBreakWindow_FailsWithoutTouchingDevice()
    {
        using var db = TestDb.CreateInMemory();
        var employeeId = Guid.NewGuid();
        var breakEnd = TimeOnly.FromDateTime(DateTime.Now.AddMinutes(-45));
        var api = new FakeBackendApiClient
        {
            LookupResult = new EmployeeLookupResult(employeeId, "E001", "Jane Doe", ShiftBreakEnd: breakEnd),
            TemplateResult = new byte[] { 1, 2, 3 },
        };
        var queue = new PunchQueueService(db);
        var service = new PunchCaptureService(
            new EmployeeDirectoryService(api, db, NullLogger<EmployeeDirectoryService>.Instance),
            new TemplateCacheService(api, db, NullLogger<TemplateCacheService>.Instance),
            new FakeFingerprintVerifier { AlwaysMatches = true },
            queue);
        var device = new FakeFingerprintDevice { NextCapture = new byte[] { 9, 9 } };

        var result = await service.CapturePunchAsync("E001", "BreakIn", device);

        Assert.False(result.Success);
        Assert.Contains("Too late", result.Message);
        Assert.Contains("Break In", result.Message);
        Assert.False(device.IsAcquired);
        Assert.Empty(await queue.GetPendingAsync());
    }

    [Fact]
    public async Task CapturePunch_UnknownEmployee_FailsWithoutEnqueueing()
    {
        using var db = TestDb.CreateInMemory();
        var api = new FakeBackendApiClient();
        var queue = new PunchQueueService(db);
        var service = new PunchCaptureService(
            new EmployeeDirectoryService(api, db, NullLogger<EmployeeDirectoryService>.Instance),
            new TemplateCacheService(api, db, NullLogger<TemplateCacheService>.Instance),
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
            new EmployeeDirectoryService(api, db, NullLogger<EmployeeDirectoryService>.Instance),
            new TemplateCacheService(api, db, NullLogger<TemplateCacheService>.Instance),
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
            new EmployeeDirectoryService(api, db, NullLogger<EmployeeDirectoryService>.Instance),
            new TemplateCacheService(api, db, NullLogger<TemplateCacheService>.Instance),
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
            new EmployeeDirectoryService(api, db, NullLogger<EmployeeDirectoryService>.Instance),
            new TemplateCacheService(api, db, NullLogger<TemplateCacheService>.Instance),
            new FakeFingerprintVerifier(),
            queue);

        var result = await service.CapturePunchAsync("E001", "In", new FakeFingerprintDevice());

        Assert.False(result.Success);
        Assert.Empty(await queue.GetPendingAsync());
    }
}
