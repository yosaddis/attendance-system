using AttendanceAgent.Data;

namespace AttendanceAgent.Services;

public interface IPunchQueueService
{
    Task EnqueueAsync(Guid employeeId, string punchType, DateTimeOffset timestamp, CancellationToken ct = default);

    /// <param name="maxCount">
    /// Caps how many of the oldest pending punches are returned, so a long outage (queue built up
    /// over days) can never make a single flush post an unbounded batch. Callers that want the
    /// rest simply leave them queued for the next call.
    /// </param>
    Task<List<QueuedPunch>> GetPendingAsync(CancellationToken ct = default, int maxCount = 500);
    Task RemoveSyncedAsync(IEnumerable<Guid> punchIds, CancellationToken ct = default);
}
