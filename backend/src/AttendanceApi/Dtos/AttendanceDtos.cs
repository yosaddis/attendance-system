namespace AttendanceApi.Dtos;

public record AttendanceRowResponse(
    Guid EmployeeId,
    string EmployeeName,
    DateOnly Date,
    Guid? ShiftId,
    DateTimeOffset? FirstIn,
    DateTimeOffset? LastOut,
    double? WorkedHours,
    bool HasShift,
    bool IsLate,
    int? LateMinutes,
    bool IsMissingCheckout,
    bool HasDoublePunch);

public record AttendanceSummaryResponse(int TotalEmployeesWithShift, int PresentCount, int AbsentCount, int LateCount);
