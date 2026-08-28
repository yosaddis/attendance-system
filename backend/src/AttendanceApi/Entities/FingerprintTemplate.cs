namespace AttendanceApi.Entities;

public class FingerprintTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EmployeeId { get; set; }
    public DeviceVendor Vendor { get; set; }
    public required byte[] TemplateDataEncrypted { get; set; }
    public DateTimeOffset EnrolledAt { get; set; } = DateTimeOffset.UtcNow;
}
