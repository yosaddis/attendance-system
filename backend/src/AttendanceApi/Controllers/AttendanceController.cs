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

        var punches = await _db.Punches
            .Where(p => p.TenantId == tenantId && p.Timestamp >= start && p.Timestamp < end)
            .ToListAsync();

        var employeeIds = punches.Select(p => p.EmployeeId).Distinct().ToList();
        var employees = await _db.Employees
            .Where(e => e.TenantId == tenantId && employeeIds.Contains(e.Id))
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
