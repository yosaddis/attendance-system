using AttendanceAgent.Data;
using Microsoft.EntityFrameworkCore;

namespace AttendanceAgent.Services;

public class PunchQueueService : IPunchQueueService
{
    private readonly AgentDbContext _db;

    public PunchQueueService(AgentDbContext db) => _db = db;

    public async Task EnqueueAsync(Guid employeeId, string punchType, DateTimeOffset timestamp, CancellationToken ct = default)
    {
        _db.QueuedPunches.Add(new QueuedPunch
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            PunchType = punchType,
            Timestamp = timestamp,
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task<List<QueuedPunch>> GetPendingAsync(CancellationToken ct = default)
    {
        var punches = await _db.QueuedPunches.ToListAsync(ct);
        return punches.OrderBy(p => p.Timestamp).ToList();
    }

    public async Task RemoveSyncedAsync(IEnumerable<Guid> punchIds, CancellationToken ct = default)
    {
        var ids = punchIds.ToList();
        var rows = await _db.QueuedPunches.Where(p => ids.Contains(p.Id)).ToListAsync(ct);
        _db.QueuedPunches.RemoveRange(rows);
        await _db.SaveChangesAsync(ct);
    }
}
