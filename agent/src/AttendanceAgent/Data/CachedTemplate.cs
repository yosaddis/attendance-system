namespace AttendanceAgent.Data;

public class CachedTemplate
{
    public Guid EmployeeId { get; set; }
    public required byte[] TemplateData { get; set; }
    public DateTimeOffset CachedAt { get; set; }
}
