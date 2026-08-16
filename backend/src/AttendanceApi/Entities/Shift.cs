namespace AttendanceApi.Entities;

public enum PunchMode { TwoPunch, FourPunch }

public class Shift
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public required string Name { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public int GraceMinutes { get; set; }
    public PunchMode PunchMode { get; set; } = PunchMode.TwoPunch;
    public TimeOnly? BreakStart { get; set; }
    public TimeOnly? BreakEnd { get; set; }
    public int? AllowedBreakMinutes { get; set; }
}
