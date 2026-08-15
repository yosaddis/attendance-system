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
        var tenant = new Tenant
        {
            Name = request.Name,
            DeviceVendor = Enum.Parse<DeviceVendor>(request.DeviceVendor),
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

        tenant.Name = request.Name;
        tenant.DeviceVendor = Enum.Parse<DeviceVendor>(request.DeviceVendor);
        await _db.SaveChangesAsync();
        return ToResponse(tenant);
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<TenantResponse>> UpdateStatus(Guid id, UpdateTenantStatusRequest request)
    {
        var tenant = await _db.Tenants.FindAsync(id);
        if (tenant is null) return NotFound();

        tenant.Status = Enum.Parse<TenantStatus>(request.Status);
        await _db.SaveChangesAsync();
        return ToResponse(tenant);
    }

    private static TenantResponse ToResponse(Tenant t) =>
        new(t.Id, t.Name, t.Status.ToString(), t.DeviceVendor.ToString(), t.CreatedAt);
}
