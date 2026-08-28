namespace AttendanceAgent.Services;

public interface ITemplateCacheService
{
    Task<byte[]?> GetTemplateAsync(Guid employeeId, CancellationToken ct = default);
    Task CacheTemplateAsync(Guid employeeId, byte[] templateData, CancellationToken ct = default);
}
