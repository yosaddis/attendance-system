namespace AttendanceAgent.Data;

public class CachedEmployee
{
    public Guid EmployeeId { get; set; }
    public required string EmployeeCode { get; set; }
    public required string Name { get; set; }
    public TimeOnly? ShiftStartTime { get; set; }
    public TimeOnly? ShiftEndTime { get; set; }
    public TimeOnly? ShiftBreakStart { get; set; }
    public TimeOnly? ShiftBreakEnd { get; set; }
    public DateTimeOffset CachedAt { get; set; }
}
