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
