namespace AttendanceApi.Dtos;

public record StationHealthResponse(string Status, Guid StationId, Guid TenantId);
