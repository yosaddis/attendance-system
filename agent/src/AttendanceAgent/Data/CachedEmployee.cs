namespace AttendanceAgent.Data;

public class CachedEmployee
{
    public Guid EmployeeId { get; set; }
    public required string EmployeeCode { get; set; }
    public required string Name { get; set; }
    public DateTimeOffset CachedAt { get; set; }
}
