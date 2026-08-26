namespace AttendanceApi.Dtos;

public record CreateEmployeeRequest(string EmployeeCode, string Name, Guid? ShiftId);
public record EmployeeResponse(Guid Id, string EmployeeCode, string Name, Guid? ShiftId, bool HasFingerprint);
public record EmployeeLookupResponse(
    Guid EmployeeId,
    string EmployeeCode,
    string Name,
    TimeOnly? ShiftStartTime = null,
    TimeOnly? ShiftEndTime = null,
    TimeOnly? ShiftBreakStart = null,
    TimeOnly? ShiftBreakEnd = null);
