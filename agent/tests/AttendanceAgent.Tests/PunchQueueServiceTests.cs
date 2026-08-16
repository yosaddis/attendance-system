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
    public async Task GetPending_ReturnsInChronologicalOrder_RegardlessOfInsertionOrder()
    {
        using var db = TestDb.CreateInMemory();
        var service = new PunchQueueService(db);
        var employeeId = Guid.NewGuid();
        var later = DateTimeOffset.UtcNow;
        var earlier = later.AddHours(-1);

        // Enqueue the later punch (A) first, then the earlier punch (B) second,
        // so insertion order is the reverse of chronological order.
        await service.EnqueueAsync(employeeId, "Out", later);
        await service.EnqueueAsync(employeeId, "In", earlier);

        var pending = await service.GetPendingAsync();

        Assert.Equal(2, pending.Count);
        Assert.Equal(earlier, pending[0].Timestamp);
        Assert.Equal(later, pending[1].Timestamp);
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
