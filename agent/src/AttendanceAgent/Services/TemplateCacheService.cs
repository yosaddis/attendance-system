using System.Net.Http;
using AttendanceAgent.Api;
using AttendanceAgent.Data;
using Microsoft.EntityFrameworkCore;

namespace AttendanceAgent.Services;

public class TemplateCacheService : ITemplateCacheService
{
    private readonly IBackendApiClient _api;
    private readonly AgentDbContext _db;

    public TemplateCacheService(IBackendApiClient api, AgentDbContext db)
    {
        _api = api;
        _db = db;
    }

    public async Task<byte[]?> GetTemplateAsync(Guid employeeId, CancellationToken ct = default)
    {
        try
        {
            var result = await _api.FetchTemplateAsync(employeeId, ct);
            if (result is not null)
            {
                await UpsertCacheAsync(employeeId, result, ct);
                return result;
            }
        }
        catch (HttpRequestException)
        {
            // offline or unreachable — fall through to the local cache
        }

        var cached = await _db.CachedTemplates.FindAsync(new object[] { employeeId }, ct);
        return cached?.TemplateData;
    }

    private async Task UpsertCacheAsync(Guid employeeId, byte[] templateData, CancellationToken ct)
    {
        var existing = await _db.CachedTemplates.FindAsync(new object[] { employeeId }, ct);
        if (existing is null)
        {
            _db.CachedTemplates.Add(new CachedTemplate { EmployeeId = employeeId, TemplateData = templateData, CachedAt = DateTimeOffset.UtcNow });
        }
        else
        {
            existing.TemplateData = templateData;
            existing.CachedAt = DateTimeOffset.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
    }
}
