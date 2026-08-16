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
[Route("api/templates")]
[Authorize(AuthenticationSchemes = StationKeySchemes.Name)]
public class TemplatesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITemplateCipher _cipher;

    public TemplatesController(AppDbContext db, ITemplateCipher cipher)
    {
        _db = db;
        _cipher = cipher;
    }

    [HttpPost]
    public async Task<IActionResult> Enroll(EnrollTemplateRequest request)
    {
        var tenantId = User.TenantId()!.Value;
        var employee = await _db.Employees.SingleOrDefaultAsync(e => e.Id == request.EmployeeId && e.TenantId == tenantId);
        if (employee is null) return NotFound();

        var station = await _db.Stations.SingleAsync(s => s.Id == User.StationId()!.Value);
        var plaintext = Convert.FromBase64String(request.TemplateData);
        var encrypted = _cipher.Encrypt(plaintext);

        var existing = await _db.FingerprintTemplates.SingleOrDefaultAsync(t => t.EmployeeId == request.EmployeeId);
        if (existing is null)
        {
            _db.FingerprintTemplates.Add(new FingerprintTemplate
            {
                EmployeeId = request.EmployeeId,
                Vendor = station.DeviceVendor,
                TemplateDataEncrypted = encrypted,
            });
        }
        else
        {
            existing.TemplateDataEncrypted = encrypted;
            existing.Vendor = station.DeviceVendor;
            existing.EnrolledAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpGet("{employeeId:guid}")]
    public async Task<ActionResult<TemplateResponse>> Fetch(Guid employeeId)
    {
        var tenantId = User.TenantId()!.Value;
        var employee = await _db.Employees.SingleOrDefaultAsync(e => e.Id == employeeId && e.TenantId == tenantId);
        if (employee is null) return NotFound();

        var template = await _db.FingerprintTemplates.SingleOrDefaultAsync(t => t.EmployeeId == employeeId);
        if (template is null) return NotFound();

        var plaintext = _cipher.Decrypt(template.TemplateDataEncrypted);
        return new TemplateResponse(employeeId, Convert.ToBase64String(plaintext), template.EnrolledAt);
    }
}
