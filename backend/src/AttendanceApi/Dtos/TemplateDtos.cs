namespace AttendanceApi.Dtos;

public record EnrollTemplateRequest(Guid EmployeeId, string TemplateData);
public record TemplateResponse(Guid EmployeeId, string TemplateData, DateTimeOffset EnrolledAt);
