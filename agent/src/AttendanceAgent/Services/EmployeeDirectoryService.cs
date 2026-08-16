using System.Net.Http;
using AttendanceAgent.Api;
using AttendanceAgent.Data;
using Microsoft.EntityFrameworkCore;

namespace AttendanceAgent.Services;

public class EmployeeDirectoryService : IEmployeeDirectoryService
{
    private readonly IBackendApiClient _api;
    private readonly AgentDbContext _db;

    public EmployeeDirectoryService(IBackendApiClient api, AgentDbContext db)
    {
        _api = api;
        _db = db;
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
        catch (HttpRequestException)
        {
            // offline or unreachable — fall through to the local cache
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
