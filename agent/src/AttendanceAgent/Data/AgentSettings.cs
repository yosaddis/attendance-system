namespace AttendanceAgent.Data;

public class AgentSettings
{
    public int Id { get; set; } = 1;
    public required string BackendBaseUrl { get; set; }
    public required string StationApiKey { get; set; }
}
