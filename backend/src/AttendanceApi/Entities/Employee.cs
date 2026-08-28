namespace AttendanceApi.Entities;

public class Employee
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public required string EmployeeCode { get; set; }
    public required string Name { get; set; }
    public Guid? ShiftId { get; set; }
}
