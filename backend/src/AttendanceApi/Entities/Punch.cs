namespace AttendanceApi.Entities;

public enum PunchType { In, BreakOut, BreakIn, Out }

public class Punch
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid StationId { get; set; }
    public PunchType PunchType { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public DateTimeOffset SyncedAt { get; set; } = DateTimeOffset.UtcNow;
}
