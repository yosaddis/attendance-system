using Microsoft.AspNetCore.Authorization;

namespace AttendanceApi.Auth;

public static class AuthorizationPolicies
{
    public const string Operator = "Operator";
    public const string TenantAdmin = "TenantAdmin";

    public static void AddAttendancePolicies(this AuthorizationOptions options)
    {
        options.AddPolicy(Operator, p => p.RequireRole("Operator"));
        options.AddPolicy(TenantAdmin, p => p.RequireRole("TenantAdmin"));
    }
}
