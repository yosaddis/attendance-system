using AttendanceApi.Auth;
using AttendanceApi.Data;
using AttendanceApi.Dtos;
using AttendanceApi.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceApi.Controllers;

[ApiController]
[Route("api/employees")]
[Authorize(Policy = AuthorizationPolicies.TenantAdmin)]
public class EmployeesController : ControllerBase
{
    private readonly AppDbContext _db;

    public EmployeesController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<List<EmployeeResponse>>> List()
    {
        var tenantId = User.TenantId()!.Value;
        var employees = await _db.Employees.Where(e => e.TenantId == tenantId).ToListAsync();

        var employeeIds = employees.Select(e => e.Id).ToList();
        var withTemplate = (await _db.FingerprintTemplates
            .Where(t => employeeIds.Contains(t.EmployeeId))
            .Select(t => t.EmployeeId)
            .ToListAsync()).ToHashSet();

        return employees.Select(e => ToResponse(e, withTemplate.Contains(e.Id))).ToList();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<EmployeeResponse>> Get(Guid id)
    {
        var tenantId = User.TenantId()!.Value;
        var employee = await _db.Employees.SingleOrDefaultAsync(e => e.Id == id && e.TenantId == tenantId);
        if (employee is null) return NotFound();

        var hasFingerprint = await _db.FingerprintTemplates.AnyAsync(t => t.EmployeeId == id);
        return ToResponse(employee, hasFingerprint);
    }

    [HttpPost]
    public async Task<ActionResult<EmployeeResponse>> Create(CreateEmployeeRequest request)
    {
        var tenantId = User.TenantId()!.Value;
        var exists = await _db.Employees.AnyAsync(e => e.TenantId == tenantId && e.EmployeeCode == request.EmployeeCode);
        if (exists) return BadRequest($"Employee code '{request.EmployeeCode}' is already in use.");

        if (request.ShiftId is not null)
        {
            var shiftExists = await _db.Shifts.AnyAsync(s => s.Id == request.ShiftId && s.TenantId == tenantId);
            if (!shiftExists) return BadRequest("Shift not found for this tenant.");
        }

        var employee = new Employee
        {
            TenantId = tenantId,
            EmployeeCode = request.EmployeeCode,
            Name = request.Name,
            ShiftId = request.ShiftId,
        };
        _db.Employees.Add(employee);
        await _db.SaveChangesAsync();
        // A brand-new employee's Id was just generated — no FingerprintTemplate row can exist for
        // it yet, so this is always false without needing a query.
        return CreatedAtAction(nameof(Get), new { id = employee.Id }, ToResponse(employee, hasFingerprint: false));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<EmployeeResponse>> Update(Guid id, CreateEmployeeRequest request)
    {
        var tenantId = User.TenantId()!.Value;
        var employee = await _db.Employees.SingleOrDefaultAsync(e => e.Id == id && e.TenantId == tenantId);
        if (employee is null) return NotFound();

        if (request.ShiftId is not null)
        {
            var shiftExists = await _db.Shifts.AnyAsync(s => s.Id == request.ShiftId && s.TenantId == tenantId);
            if (!shiftExists) return BadRequest("Shift not found for this tenant.");
        }

        employee.Name = request.Name;
        employee.ShiftId = request.ShiftId;
        await _db.SaveChangesAsync();

        var hasFingerprint = await _db.FingerprintTemplates.AnyAsync(t => t.EmployeeId == employee.Id);
        return ToResponse(employee, hasFingerprint);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var tenantId = User.TenantId()!.Value;
        var employee = await _db.Employees.SingleOrDefaultAsync(e => e.Id == id && e.TenantId == tenantId);
        if (employee is null) return NotFound();

        var template = await _db.FingerprintTemplates.SingleOrDefaultAsync(t => t.EmployeeId == id);
        if (template is not null) _db.FingerprintTemplates.Remove(template);

        _db.Employees.Remove(employee);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static EmployeeResponse ToResponse(Employee e, bool hasFingerprint) => new(e.Id, e.EmployeeCode, e.Name, e.ShiftId, hasFingerprint);
}
