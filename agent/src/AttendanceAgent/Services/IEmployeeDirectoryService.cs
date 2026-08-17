using AttendanceAgent.Api;

namespace AttendanceAgent.Services;

public interface IEmployeeDirectoryService
{
    Task<EmployeeLookupResult?> ResolveAsync(string code, CancellationToken ct = default);
}
