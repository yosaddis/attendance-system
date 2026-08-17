namespace AttendanceAgent.Data;

public class QueuedPunch
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public required string PunchType { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}
