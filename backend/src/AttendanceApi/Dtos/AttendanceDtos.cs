namespace AttendanceApi.Dtos;

public record DailyAttendanceResponse(Guid EmployeeId, string EmployeeName, DateTimeOffset? FirstIn, DateTimeOffset? LastOut);
