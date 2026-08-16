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
        return employees.Select(ToResponse).ToList();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<EmployeeResponse>> Get(Guid id)
    {
        var tenantId = User.TenantId()!.Value;
        var employee = await _db.Employees.SingleOrDefaultAsync(e => e.Id == id && e.TenantId == tenantId);
        return employee is null ? NotFound() : ToResponse(employee);
    }

    [HttpPost]
    public async Task<ActionResult<EmployeeResponse>> Create(CreateEmployeeRequest request)
    {
        var tenantId = User.TenantId()!.Value;
        var exists = await _db.Employees.AnyAsync(e => e.TenantId == tenantId && e.EmployeeCode == request.EmployeeCode);
        if (exists) return BadRequest($"Employee code '{request.EmployeeCode}' is already in use.");

        var employee = new Employee
        {
            TenantId = tenantId,
            EmployeeCode = request.EmployeeCode,
            Name = request.Name,
            ShiftId = request.ShiftId,
        };
        _db.Employees.Add(employee);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = employee.Id }, ToResponse(employee));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<EmployeeResponse>> Update(Guid id, CreateEmployeeRequest request)
    {
        var tenantId = User.TenantId()!.Value;
        var employee = await _db.Employees.SingleOrDefaultAsync(e => e.Id == id && e.TenantId == tenantId);
        if (employee is null) return NotFound();

        employee.Name = request.Name;
        employee.ShiftId = request.ShiftId;
        await _db.SaveChangesAsync();
        return ToResponse(employee);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var tenantId = User.TenantId()!.Value;
        var employee = await _db.Employees.SingleOrDefaultAsync(e => e.Id == id && e.TenantId == tenantId);
        if (employee is null) return NotFound();

        _db.Employees.Remove(employee);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static EmployeeResponse ToResponse(Employee e) => new(e.Id, e.EmployeeCode, e.Name, e.ShiftId);
}
