namespace AttendanceAgent.Api;

public record EmployeeLookupResult(Guid EmployeeId, string EmployeeCode, string Name);

internal record TemplateFetchResponse(Guid EmployeeId, string TemplateData, DateTimeOffset EnrolledAt);
internal record PunchPayload(Guid Id, Guid EmployeeId, string PunchType, DateTimeOffset Timestamp);
internal record PunchBatchPayload(List<PunchPayload> Punches);
