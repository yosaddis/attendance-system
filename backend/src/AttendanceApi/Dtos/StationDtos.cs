namespace AttendanceApi.Dtos;

public record CreateStationRequest(string Name, string DeviceVendor);
public record StationResponse(Guid Id, Guid TenantId, string Name, string DeviceVendor, DateTimeOffset? LastSyncedAt);
public record StationCreatedResponse(Guid Id, string Name, string ApiKey);
