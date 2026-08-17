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
        return cached is null ? null : new EmployeeLookupResult(cached.EmployeeId, cached.EmployeeCode, cached.Name);
    }

    private async Task UpsertCacheAsync(EmployeeLookupResult result, CancellationToken ct)
    {
        var existing = await _db.CachedEmployees.FindAsync(new object[] { result.EmployeeId }, ct);
        if (existing is null)
        {
            _db.CachedEmployees.Add(new CachedEmployee
            {
                EmployeeId = result.EmployeeId,
                EmployeeCode = result.EmployeeCode,
                Name = result.Name,
                CachedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            existing.EmployeeCode = result.EmployeeCode;
            existing.Name = result.Name;
            existing.CachedAt = DateTimeOffset.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
    }
}
