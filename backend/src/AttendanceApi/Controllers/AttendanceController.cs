using AttendanceApi.Auth;
using AttendanceApi.Data;
using AttendanceApi.Dtos;
using AttendanceApi.Entities;
using AttendanceApi.Services;
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
    public async Task<ActionResult<List<AttendanceRowResponse>>> Daily([FromQuery] DateOnly date)
    {
        var tenantId = User.TenantId()!.Value;
        return await BuildDailyRowsAsync(tenantId, date);
    }

    private async Task<List<AttendanceRowResponse>> BuildDailyRowsAsync(Guid tenantId, DateOnly date)
    {
        var offset = AttendanceAnalysisService.DefaultTenantOffset;
        var start = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), offset);
        var end = start.AddDays(1);

        var punches = await _db.Punches
            .Where(p => p.TenantId == tenantId && p.Timestamp >= start && p.Timestamp < end)
            .ToListAsync();

        var employees = await _db.Employees.Where(e => e.TenantId == tenantId).ToListAsync();
        var shifts = await _db.Shifts.Where(s => s.TenantId == tenantId).ToDictionaryAsync(s => s.Id);
        var employeesById = employees.ToDictionary(e => e.Id);

        // Union, not a plain join: an employee with a shift but zero punches must still appear
        // (absent), and a punch pointing at an employee id outside this tenant must still appear
        // too (defensive — see DailyView_NameLookupIsTenantScoped_EvenForBadDataWithForeignEmployeeId).
        var employeeIds = employees.Select(e => e.Id).Union(punches.Select(p => p.EmployeeId));

        var rows = new List<AttendanceRowResponse>();
        foreach (var id in employeeIds)
        {
            var employee = employeesById.GetValueOrDefault(id);
            var shift = employee?.ShiftId is Guid shiftId ? shifts.GetValueOrDefault(shiftId) : null;
            var dayPunches = punches.Where(p => p.EmployeeId == id).ToList();
            var result = AttendanceAnalysisService.Analyze(dayPunches, shift);

            rows.Add(new AttendanceRowResponse(
                id,
                employee?.Name ?? "Unknown",
                date,
                employee?.ShiftId,
                result.FirstIn,
                result.LastOut,
                result.WorkedHours,
                result.HasShift,
                result.IsLate,
                result.LateMinutes,
                result.IsMissingCheckout,
                result.HasDoublePunch));
        }

        return rows;
    }
}
