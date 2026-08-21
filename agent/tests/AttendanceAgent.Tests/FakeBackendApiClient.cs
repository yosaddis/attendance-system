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
    public LoginResult? LoginResult { get; set; }
    public bool ThrowOnLogin { get; set; }
    public bool EnrollTemplateResult { get; set; } = true;
    public bool ThrowOnEnrollTemplate { get; set; }
    public (Guid EmployeeId, byte[] TemplateData)? LastEnrolledTemplate { get; private set; }
    public Guid? StationTenantId { get; set; }

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

    public Task<LoginResult?> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        if (ThrowOnLogin) throw new HttpRequestException("offline");
        return Task.FromResult(LoginResult);
    }

    public Task<bool> EnrollTemplateAsync(Guid employeeId, byte[] templateData, CancellationToken ct = default)
    {
        if (ThrowOnEnrollTemplate) throw new HttpRequestException("offline");
        LastEnrolledTemplate = (employeeId, templateData);
        return Task.FromResult(EnrollTemplateResult);
    }

    public Task<Guid?> GetStationTenantIdAsync(CancellationToken ct = default) => Task.FromResult(StationTenantId);
}
