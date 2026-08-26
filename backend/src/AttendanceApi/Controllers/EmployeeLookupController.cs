using AttendanceApi.Auth;
using AttendanceApi.Data;
using AttendanceApi.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceApi.Controllers;

[ApiController]
[Route("api/employees/lookup")]
[Authorize(AuthenticationSchemes = StationKeySchemes.Name)]
public class EmployeeLookupController : ControllerBase
{
    private readonly AppDbContext _db;

    public EmployeeLookupController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<EmployeeLookupResponse>> Lookup([FromQuery] string code)
    {
        var tenantId = User.TenantId()!.Value;
        var employee = await _db.Employees.SingleOrDefaultAsync(e => e.TenantId == tenantId && e.EmployeeCode == code);
        if (employee is null) return NotFound();

        var shift = employee.ShiftId is null
            ? null
            : await _db.Shifts.SingleOrDefaultAsync(s => s.Id == employee.ShiftId);

        return new EmployeeLookupResponse(
            employee.Id,
            employee.EmployeeCode,
            employee.Name,
            shift?.StartTime,
            shift?.EndTime,
            shift?.BreakStart,
            shift?.BreakEnd);
    }
}
