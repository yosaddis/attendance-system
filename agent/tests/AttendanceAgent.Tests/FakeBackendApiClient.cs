using AttendanceAgent.Api;
using AttendanceAgent.Data;

namespace AttendanceAgent.Tests;

public class FakeBackendApiClient : IBackendApiClient
{
    public EmployeeLookupResult? LookupResult { get; set; }
    public bool ThrowOnLookup { get; set; }
    public Func<Exception>? LookupExceptionToThrow { get; set; }
    public byte[]? TemplateResult { get; set; }
    public bool ThrowOnTemplate { get; set; }
    public Func<Exception>? TemplateExceptionToThrow { get; set; }
    public PunchBatchSubmitResult SubmitResult { get; set; } = PunchBatchSubmitResult.Accepted;
    public bool ThrowOnSubmit { get; set; }
    public Func<Exception>? SubmitExceptionToThrow { get; set; }

    public Task<EmployeeLookupResult?> LookupEmployeeAsync(string code, CancellationToken ct = default)
    {
        if (LookupExceptionToThrow is not null) throw LookupExceptionToThrow();
        if (ThrowOnLookup) throw new HttpRequestException("offline");
        return Task.FromResult(LookupResult);
    }

    public Task<byte[]?> FetchTemplateAsync(Guid employeeId, CancellationToken ct = default)
    {
        if (TemplateExceptionToThrow is not null) throw TemplateExceptionToThrow();
        if (ThrowOnTemplate) throw new HttpRequestException("offline");
        return Task.FromResult(TemplateResult);
    }

    public Task<PunchBatchSubmitResult> SubmitPunchesAsync(IReadOnlyList<QueuedPunch> punches, CancellationToken ct = default)
    {
        if (SubmitExceptionToThrow is not null) throw SubmitExceptionToThrow();
        if (ThrowOnSubmit) throw new HttpRequestException("offline");
        return Task.FromResult(SubmitResult);
    }
}
