namespace AttendanceApi.Dtos;

public record CreateEmployeeRequest(string EmployeeCode, string Name, Guid? ShiftId);
public record EmployeeResponse(Guid Id, string EmployeeCode, string Name, Guid? ShiftId);
public record EmployeeLookupResponse(Guid EmployeeId, string EmployeeCode, string Name);
