namespace AttendanceAgent.Api;

public record EmployeeLookupResult(Guid EmployeeId, string EmployeeCode, string Name);
public record LoginResult(string Role);

internal record TemplateFetchResponse(Guid EmployeeId, string TemplateData, DateTimeOffset EnrolledAt);
internal record PunchPayload(Guid Id, Guid EmployeeId, string PunchType, DateTimeOffset Timestamp);
internal record PunchBatchPayload(List<PunchPayload> Punches);
internal record LoginRequestPayload(string Email, string Password);
internal record EnrollTemplateRequestPayload(Guid EmployeeId, string TemplateData);

/// <summary>
/// Outcome of submitting a punch batch, distinguishing a permanent, batch-level rejection from a
/// transient failure. The backend rejects an entire batch (HTTP 400) if ANY single punch in it is
/// invalid (unknown employee for the tenant, duplicate id, bad punch type) — that's a permanent
/// verdict retrying won't change, unlike a 5xx/network failure which is worth retrying as-is.
/// </summary>
public enum PunchBatchSubmitResult
{
    /// <summary>The backend accepted the batch; safe to remove these punches from the local queue.</summary>
    Accepted,

    /// <summary>
    /// The backend explicitly rejected the batch as malformed (HTTP 400). Retrying the same batch
    /// will fail again — the caller should drop the punches to avoid wedging the queue on a single
    /// poison punch.
    /// </summary>
    RejectedByBackend,

    /// <summary>A transient/server failure (5xx or similar); leave the batch queued and retry later.</summary>
    TransientFailure,
}
