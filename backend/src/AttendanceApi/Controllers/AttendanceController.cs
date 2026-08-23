using System.Text;
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

    [HttpGet("summary")]
    public async Task<ActionResult<AttendanceSummaryResponse>> Summary([FromQuery] DateOnly date)
    {
        var tenantId = User.TenantId()!.Value;
        var rows = await BuildDailyRowsAsync(tenantId, date);
        var withShift = rows.Where(r => r.HasShift).ToList();

        var present = withShift.Count(r => r.FirstIn is not null);
        var absent = withShift.Count(r => r.FirstIn is null);
        var late = withShift.Count(r => r.IsLate);

        return new AttendanceSummaryResponse(withShift.Count, present, absent, late);
    }

    [HttpGet("report")]
    public async Task<ActionResult<List<AttendanceRowResponse>>> Report([FromQuery] DateOnly from, [FromQuery] DateOnly to)
    {
        var validation = ValidateRange(from, to);
        if (validation is not null) return validation;

        var tenantId = User.TenantId()!.Value;
        return await BuildReportRowsAsync(tenantId, from, to);
    }

    [HttpGet("report/export")]
    public async Task<IActionResult> ReportExport([FromQuery] DateOnly from, [FromQuery] DateOnly to)
    {
        var validation = ValidateRange(from, to);
        if (validation is not null) return validation;

        var tenantId = User.TenantId()!.Value;
        var rows = await BuildReportRowsAsync(tenantId, from, to);
        var bytes = Encoding.UTF8.GetBytes(ToCsv(rows));
        return File(bytes, "text/csv", $"attendance-{from:yyyy-MM-dd}-to-{to:yyyy-MM-dd}.csv");
    }

    private ActionResult? ValidateRange(DateOnly from, DateOnly to)
    {
        if (to < from) return BadRequest("'to' must not be before 'from'.");
        var daysInclusive = to.DayNumber - from.DayNumber + 1;
        if (daysInclusive > 90) return BadRequest("Date range cannot exceed 90 days.");
        return null;
    }

    private async Task<List<AttendanceRowResponse>> BuildReportRowsAsync(Guid tenantId, DateOnly from, DateOnly to)
    {
        var rows = new List<AttendanceRowResponse>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var dayRows = await BuildDailyRowsAsync(tenantId, date);
            rows.AddRange(dayRows.Where(r => r.HasShift || r.FirstIn is not null || r.LastOut is not null));
        }
        return rows;
    }

    private static string ToCsv(List<AttendanceRowResponse> rows)
    {
        var sb = new StringBuilder();
        sb.Append("Employee,Date,First In,Last Out,Worked Hours,Late (minutes),Missing Checkout,Double Punch\n");

        var offset = AttendanceAnalysisService.DefaultTenantOffset;
        foreach (var r in rows)
        {
            var fields = new[]
            {
                CsvEscape(r.EmployeeName),
                r.Date.ToString("yyyy-MM-dd"),
                r.FirstIn?.ToOffset(offset).ToString("HH:mm") ?? "",
                r.LastOut?.ToOffset(offset).ToString("HH:mm") ?? "",
                r.WorkedHours?.ToString("0.00") ?? "",
                r.IsLate ? r.LateMinutes.ToString() ?? "" : "",
                r.IsMissingCheckout ? "yes" : "",
                r.HasDoublePunch ? "yes" : "",
            };
            sb.Append(string.Join(",", fields));
            sb.Append('\n');
        }

        return sb.ToString();
    }

    private static string CsvEscape(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;

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
