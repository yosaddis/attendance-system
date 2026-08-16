using AttendanceAgent.Data;

namespace AttendanceAgent.Api;

public interface IBackendApiClient
{
    Task<EmployeeLookupResult?> LookupEmployeeAsync(string code, CancellationToken ct = default);
    Task<byte[]?> FetchTemplateAsync(Guid employeeId, CancellationToken ct = default);
    Task<bool> SubmitPunchesAsync(IReadOnlyList<QueuedPunch> punches, CancellationToken ct = default);
}
