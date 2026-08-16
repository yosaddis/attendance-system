namespace AttendanceApi.Dtos;

public record CreateShiftRequest(
    string Name, TimeOnly StartTime, TimeOnly EndTime, int GraceMinutes,
    string PunchMode, TimeOnly? BreakStart, TimeOnly? BreakEnd, int? AllowedBreakMinutes);

public record ShiftResponse(
    Guid Id, string Name, TimeOnly StartTime, TimeOnly EndTime, int GraceMinutes,
    string PunchMode, TimeOnly? BreakStart, TimeOnly? BreakEnd, int? AllowedBreakMinutes);
