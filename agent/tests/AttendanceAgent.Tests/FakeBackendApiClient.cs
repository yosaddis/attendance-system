using AttendanceAgent.Api;
using AttendanceAgent.Data;

namespace AttendanceAgent.Tests;

public class FakeBackendApiClient : IBackendApiClient
{
    public EmployeeLookupResult? LookupResult { get; set; }
    public bool ThrowOnLookup { get; set; }
    public byte[]? TemplateResult { get; set; }
    public bool ThrowOnTemplate { get; set; }
    public bool SubmitResult { get; set; } = true;
    public bool ThrowOnSubmit { get; set; }

    public Task<EmployeeLookupResult?> LookupEmployeeAsync(string code, CancellationToken ct = default)
    {
        if (ThrowOnLookup) throw new HttpRequestException("offline");
        return Task.FromResult(LookupResult);
    }

    public Task<byte[]?> FetchTemplateAsync(Guid employeeId, CancellationToken ct = default)
    {
        if (ThrowOnTemplate) throw new HttpRequestException("offline");
        return Task.FromResult(TemplateResult);
    }

    public Task<bool> SubmitPunchesAsync(IReadOnlyList<QueuedPunch> punches, CancellationToken ct = default)
    {
        if (ThrowOnSubmit) throw new HttpRequestException("offline");
        return Task.FromResult(SubmitResult);
    }
}
