namespace AttendanceApi.Entities;

public enum TenantStatus { Active, Grace, Suspended }
public enum DeviceVendor { Zk4500, Secugen }

public class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public TenantStatus Status { get; set; } = TenantStatus.Active;
    public DeviceVendor DeviceVendor { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
