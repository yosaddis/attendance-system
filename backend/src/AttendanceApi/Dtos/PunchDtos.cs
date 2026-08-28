namespace AttendanceApi.Dtos;

public record PunchDto(Guid Id, Guid EmployeeId, string PunchType, DateTimeOffset Timestamp);
public record PunchBatchRequest(List<PunchDto> Punches);
public record PunchBatchResponse(List<Guid> AcceptedIds);
