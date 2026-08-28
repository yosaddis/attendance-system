using System.Net.Http;
using AttendanceAgent.Api;
using AttendanceAgent.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AttendanceAgent.Services;

public class EmployeeDirectoryService : IEmployeeDirectoryService
{
    private readonly IBackendApiClient _api;
    private readonly AgentDbContext _db;
    private readonly ILogger<EmployeeDirectoryService> _logger;

    public EmployeeDirectoryService(IBackendApiClient api, AgentDbContext db, ILogger<EmployeeDirectoryService> logger)
    {
        _api = api;
        _db = db;
        _logger = logger;
    }

    public async Task<EmployeeLookupResult?> ResolveAsync(string code, CancellationToken ct = default)
    {
        try
        {
            var result = await _api.LookupEmployeeAsync(code, ct);
            if (result is not null)
            {
                await UpsertCacheAsync(result, ct);
                return result;
            }

            // The backend call SUCCEEDED (no exception) and explicitly said "not found" — a
            // definitive verdict, not "backend unreachable." Falling through to the cache here
            // would let a terminated employee whose record was deleted server-side keep punching
            // indefinitely from any station that ever cached them. Purge the stale entry instead of
            // reading it.
            await PurgeCachedEmployeeAsync(code, ct);
            return null;
        }
        // The backend can be unreachable/unusable in more ways than HttpRequestException: HttpClient
        // timeouts surface as TaskCanceledException, a malformed/unexpected response body as
        // JsonException, a bad configured URL as UriFormatException, etc. Treat any of these as
        // "couldn't reach/use the backend right now" and fall back to the local cache, but let a
        // deliberate caller-requested cancellation (e.g. app shutdown) propagate normally.
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Employee lookup for code '{EmployeeCode}' failed against the backend; falling back to local cache.", code);
        }

        var cached = await _db.CachedEmployees.SingleOrDefaultAsync(e => e.EmployeeCode == code, ct);
        return cached is null ? null : new EmployeeLookupResult(
            cached.EmployeeId, cached.EmployeeCode, cached.Name,
            cached.ShiftStartTime, cached.ShiftEndTime, cached.ShiftBreakStart, cached.ShiftBreakEnd);
    }

    private async Task PurgeCachedEmployeeAsync(string code, CancellationToken ct)
    {
        var cached = await _db.CachedEmployees.SingleOrDefaultAsync(e => e.EmployeeCode == code, ct);
        if (cached is not null)
        {
            _db.CachedEmployees.Remove(cached);
            await _db.SaveChangesAsync(ct);
        }
    }

    private async Task UpsertCacheAsync(EmployeeLookupResult result, CancellationToken ct)
    {
        // CachedEmployees has a unique index on EmployeeCode, but the id-keyed lookup below can't
        // see a code collision: if the code was reassigned server-side (old employee deleted, a new
        // employee created with the same code), the OLD row for that code is keyed by a different
        // EmployeeId and would still be sitting in the cache. Remove it first — in its own
        // SaveChanges, so the delete is committed before the insert below runs and can never
        // collide with it on the unique index.
        var staleByCode = await _db.CachedEmployees
            .SingleOrDefaultAsync(e => e.EmployeeCode == result.EmployeeCode && e.EmployeeId != result.EmployeeId, ct);
        if (staleByCode is not null)
        {
            _db.CachedEmployees.Remove(staleByCode);
            await _db.SaveChangesAsync(ct);
        }

        var existing = await _db.CachedEmployees.FindAsync(new object[] { result.EmployeeId }, ct);
        if (existing is null)
        {
            _db.CachedEmployees.Add(new CachedEmployee
            {
                EmployeeId = result.EmployeeId,
                EmployeeCode = result.EmployeeCode,
                Name = result.Name,
                ShiftStartTime = result.ShiftStartTime,
                ShiftEndTime = result.ShiftEndTime,
                ShiftBreakStart = result.ShiftBreakStart,
                ShiftBreakEnd = result.ShiftBreakEnd,
                CachedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            existing.EmployeeCode = result.EmployeeCode;
            existing.Name = result.Name;
            existing.ShiftStartTime = result.ShiftStartTime;
            existing.ShiftEndTime = result.ShiftEndTime;
            existing.ShiftBreakStart = result.ShiftBreakStart;
            existing.ShiftBreakEnd = result.ShiftBreakEnd;
            existing.CachedAt = DateTimeOffset.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
    }
}
