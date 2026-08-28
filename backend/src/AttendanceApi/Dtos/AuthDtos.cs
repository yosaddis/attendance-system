namespace AttendanceApi.Dtos;

public record LoginRequest(string Email, string Password);
public record LoginResponse(string Token, string Role, Guid? TenantId);
