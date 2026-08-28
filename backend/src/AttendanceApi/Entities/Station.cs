namespace AttendanceApi.Entities;

public class Station
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public required string Name { get; set; }
    public DeviceVendor DeviceVendor { get; set; }
    public required string ApiKeyHash { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
}
