namespace AttendanceApi.Entities;

public enum UserRole { Operator, TenantAdmin }

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public UserRole Role { get; set; }
    public Guid? TenantId { get; set; }
}
