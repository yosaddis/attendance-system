using AttendanceAgent.Services;
using Xunit;

namespace AttendanceAgent.Tests;

public class PunchQueueServiceTests
{
    [Fact]
    public async Task Enqueue_ThenGetPending_ReturnsIt()
    {
        using var db = TestDb.CreateInMemory();
        var service = new PunchQueueService(db);
        var employeeId = Guid.NewGuid();

        await service.EnqueueAsync(employeeId, "In", DateTimeOffset.UtcNow);
        var pending = await service.GetPendingAsync();

        Assert.Single(pending);
        Assert.Equal(employeeId, pending[0].EmployeeId);
    }

    [Fact]
    public async Task RemoveSynced_DeletesOnlyGivenIds()
    {
        using var db = TestDb.CreateInMemory();
        var service = new PunchQueueService(db);
        var employeeId = Guid.NewGuid();
        await service.EnqueueAsync(employeeId, "In", DateTimeOffset.UtcNow);
        await service.EnqueueAsync(employeeId, "Out", DateTimeOffset.UtcNow);
        var pending = await service.GetPendingAsync();

        await service.RemoveSyncedAsync(new[] { pending[0].Id });

        var remaining = await service.GetPendingAsync();
        Assert.Single(remaining);
        Assert.Equal(pending[1].Id, remaining[0].Id);
    }
}
