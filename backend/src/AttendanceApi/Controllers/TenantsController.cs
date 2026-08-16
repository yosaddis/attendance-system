using AttendanceApi.Data;
using AttendanceApi.Dtos;
using AttendanceApi.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceApi.Controllers;

[ApiController]
[Route("api/tenants")]
public class TenantsController : ControllerBase
{
    private readonly AppDbContext _db;

    public TenantsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<List<TenantResponse>>> List()
    {
        var tenants = await _db.Tenants.ToListAsync();
        return tenants.Select(ToResponse).ToList();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TenantResponse>> Get(Guid id)
    {
        var tenant = await _db.Tenants.FindAsync(id);
        return tenant is null ? NotFound() : ToResponse(tenant);
    }

    [HttpPost]
    public async Task<ActionResult<TenantResponse>> Create(CreateTenantRequest request)
    {
        if (!Enum.TryParse<DeviceVendor>(request.DeviceVendor, out var vendor) || !Enum.IsDefined(vendor))
            return BadRequest($"Invalid device vendor '{request.DeviceVendor}'.");

        var tenant = new Tenant
        {
            Name = request.Name,
            DeviceVendor = vendor,
        };
        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = tenant.Id }, ToResponse(tenant));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TenantResponse>> Update(Guid id, UpdateTenantRequest request)
    {
        var tenant = await _db.Tenants.FindAsync(id);
        if (tenant is null) return NotFound();

        if (!Enum.TryParse<DeviceVendor>(request.DeviceVendor, out var vendor) || !Enum.IsDefined(vendor))
            return BadRequest($"Invalid device vendor '{request.DeviceVendor}'.");

        tenant.Name = request.Name;
        tenant.DeviceVendor = vendor;
        await _db.SaveChangesAsync();
        return ToResponse(tenant);
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<TenantResponse>> UpdateStatus(Guid id, UpdateTenantStatusRequest request)
    {
        var tenant = await _db.Tenants.FindAsync(id);
        if (tenant is null) return NotFound();

        if (!Enum.TryParse<TenantStatus>(request.Status, out var status) || !Enum.IsDefined(status))
            return BadRequest($"Invalid status '{request.Status}'.");

        tenant.Status = status;
        await _db.SaveChangesAsync();
        return ToResponse(tenant);
    }

    private static TenantResponse ToResponse(Tenant t) =>
        new(t.Id, t.Name, t.Status.ToString(), t.DeviceVendor.ToString(), t.CreatedAt);
}
