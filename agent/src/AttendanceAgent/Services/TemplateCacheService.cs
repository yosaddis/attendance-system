using System.Net.Http;
using AttendanceAgent.Api;
using AttendanceAgent.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AttendanceAgent.Services;

public class TemplateCacheService : ITemplateCacheService
{
    private readonly IBackendApiClient _api;
    private readonly AgentDbContext _db;
    private readonly ILogger<TemplateCacheService> _logger;

    public TemplateCacheService(IBackendApiClient api, AgentDbContext db, ILogger<TemplateCacheService> logger)
    {
        _api = api;
        _db = db;
        _logger = logger;
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

            // The backend call SUCCEEDED (no exception) and explicitly said "not found" — a
            // definitive verdict, not "backend unreachable." Purge any stale cached template
            // instead of falling through to it, the same way EmployeeDirectoryService.ResolveAsync
            // does for a deleted employee.
            await PurgeCachedTemplateAsync(employeeId, ct);
            return null;
        }
        // See EmployeeDirectoryService.ResolveAsync for why this is widened beyond
        // HttpRequestException: timeouts, malformed responses, and corrupt base64 template data
        // are all "the backend is currently unusable" failures, not just transport-level ones.
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Template fetch for employee '{EmployeeId}' failed against the backend; falling back to local cache.", employeeId);
        }

        var cached = await _db.CachedTemplates.FindAsync(new object[] { employeeId }, ct);
        return cached?.TemplateData;
    }

    private async Task PurgeCachedTemplateAsync(Guid employeeId, CancellationToken ct)
    {
        var cached = await _db.CachedTemplates.FindAsync(new object[] { employeeId }, ct);
        if (cached is not null)
        {
            _db.CachedTemplates.Remove(cached);
            await _db.SaveChangesAsync(ct);
        }
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
