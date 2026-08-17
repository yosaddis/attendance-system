namespace AttendanceAgent.Services;

public interface ITemplateCacheService
{
    Task<byte[]?> GetTemplateAsync(Guid employeeId, CancellationToken ct = default);
}
