namespace AttendanceApi.Dtos;

public record CreateTenantRequest(string Name, string DeviceVendor);
public record UpdateTenantRequest(string Name, string DeviceVendor);
public record UpdateTenantStatusRequest(string Status);
public record TenantResponse(Guid Id, string Name, string Status, string DeviceVendor, DateTimeOffset CreatedAt);
public record CreateTenantAdminRequest(string Email, string Password);
public record TenantAdminResponse(Guid Id, string Email, Guid TenantId);
