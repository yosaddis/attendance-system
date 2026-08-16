using AttendanceApi.Auth;
using AttendanceApi.Data;
using AttendanceApi.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceApi.Controllers;

[ApiController]
[Route("api/attendance")]
[Authorize(Policy = AuthorizationPolicies.TenantAdmin)]
public class AttendanceController : ControllerBase
{
    private readonly AppDbContext _db;

    public AttendanceController(AppDbContext db) => _db = db;

    [HttpGet("daily")]
    public async Task<ActionResult<List<DailyAttendanceResponse>>> Daily([FromQuery] DateOnly date)
    {
        var tenantId = User.TenantId()!.Value;
        var start = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = start.AddDays(1);

        // The tenant filter runs server-side (critical for tenant isolation — never trust
        // a client-supplied scope). The date-range filter runs client-side after
        // materialization: EF Core's Sqlite provider (used in tests) cannot translate
        // relational (>=/<) comparisons on DateTimeOffset columns, throwing at query time;
        // Npgsql (production) can, but this keeps behavior identical across providers.
        var punches = (await _db.Punches
                .Where(p => p.TenantId == tenantId)
                .ToListAsync())
            .Where(p => p.Timestamp >= start && p.Timestamp < end)
            .ToList();

        var employeeIds = punches.Select(p => p.EmployeeId).Distinct().ToList();
        var employees = await _db.Employees
            .Where(e => employeeIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.Name);

        var rows = punches
            .GroupBy(p => p.EmployeeId)
            .Select(g => new DailyAttendanceResponse(
                g.Key,
                employees.GetValueOrDefault(g.Key, "Unknown"),
                g.Min(p => (DateTimeOffset?)p.Timestamp),
                g.Max(p => (DateTimeOffset?)p.Timestamp)))
            .ToList();

        return rows;
    }
}
