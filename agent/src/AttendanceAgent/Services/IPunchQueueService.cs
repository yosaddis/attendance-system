using AttendanceAgent.Data;

namespace AttendanceAgent.Services;

public interface IPunchQueueService
{
    Task EnqueueAsync(Guid employeeId, string punchType, DateTimeOffset timestamp, CancellationToken ct = default);
    Task<List<QueuedPunch>> GetPendingAsync(CancellationToken ct = default);
    Task RemoveSyncedAsync(IEnumerable<Guid> punchIds, CancellationToken ct = default);
}
