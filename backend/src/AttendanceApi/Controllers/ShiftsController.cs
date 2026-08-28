using AttendanceApi.Auth;
using AttendanceApi.Data;
using AttendanceApi.Dtos;
using AttendanceApi.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceApi.Controllers;

[ApiController]
[Route("api/shifts")]
[Authorize(Policy = AuthorizationPolicies.TenantAdmin)]
public class ShiftsController : ControllerBase
{
    private readonly AppDbContext _db;

    public ShiftsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<List<ShiftResponse>>> List()
    {
        var tenantId = User.TenantId()!.Value;
        var shifts = await _db.Shifts.Where(s => s.TenantId == tenantId).ToListAsync();
        return shifts.Select(ToResponse).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<ShiftResponse>> Create(CreateShiftRequest request)
    {
        if (!Enum.TryParse<PunchMode>(request.PunchMode, out var punchMode) || !Enum.IsDefined(punchMode))
            return BadRequest($"Invalid punch mode '{request.PunchMode}'.");

        var tenantId = User.TenantId()!.Value;
        var shift = new Shift
        {
            TenantId = tenantId,
            Name = request.Name,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            GraceMinutes = request.GraceMinutes,
            PunchMode = punchMode,
            BreakStart = request.BreakStart,
            BreakEnd = request.BreakEnd,
            AllowedBreakMinutes = request.AllowedBreakMinutes,
        };
        _db.Shifts.Add(shift);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(List), null, ToResponse(shift));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ShiftResponse>> Update(Guid id, CreateShiftRequest request)
    {
        if (!Enum.TryParse<PunchMode>(request.PunchMode, out var punchMode) || !Enum.IsDefined(punchMode))
            return BadRequest($"Invalid punch mode '{request.PunchMode}'.");

        var tenantId = User.TenantId()!.Value;
        var shift = await _db.Shifts.SingleOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId);
        if (shift is null) return NotFound();

        shift.Name = request.Name;
        shift.StartTime = request.StartTime;
        shift.EndTime = request.EndTime;
        shift.GraceMinutes = request.GraceMinutes;
        shift.PunchMode = punchMode;
        shift.BreakStart = request.BreakStart;
        shift.BreakEnd = request.BreakEnd;
        shift.AllowedBreakMinutes = request.AllowedBreakMinutes;
        await _db.SaveChangesAsync();
        return ToResponse(shift);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var tenantId = User.TenantId()!.Value;
        var shift = await _db.Shifts.SingleOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId);
        if (shift is null) return NotFound();

        _db.Shifts.Remove(shift);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static ShiftResponse ToResponse(Shift s) => new(
        s.Id, s.Name, s.StartTime, s.EndTime, s.GraceMinutes, s.PunchMode.ToString(),
        s.BreakStart, s.BreakEnd, s.AllowedBreakMinutes);
}
