using System.Security.Claims;
using System.Text.Encodings.Web;
using AttendanceApi.Data;
using AttendanceApi.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AttendanceApi.Auth;

public static class StationKeySchemes
{
    public const string Name = "StationKey";
}

public class StationKeyAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly AppDbContext _db;

    public StationKeyAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        AppDbContext db)
        : base(options, logger, encoder)
    {
        _db = db;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Station-Key", out var key) || string.IsNullOrEmpty(key))
            return AuthenticateResult.NoResult();

        var hash = StationKeyGenerator.Hash(key!);
        var station = await _db.Stations.SingleOrDefaultAsync(s => s.ApiKeyHash == hash);
        if (station is null) return AuthenticateResult.Fail("Invalid station key");

        var claims = new[]
        {
            new Claim("tenant_id", station.TenantId.ToString()),
            new Claim("station_id", station.Id.ToString()),
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }
}
