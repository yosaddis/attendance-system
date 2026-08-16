using System.Security.Claims;

namespace AttendanceApi.Auth;

public static class ClaimsPrincipalExtensions
{
    public static Guid? TenantId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirst("tenant_id")?.Value;
        return value is null ? null : Guid.Parse(value);
    }
}
