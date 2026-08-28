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
[Route("api/tenants/{tenantId:guid}/stations")]
[Authorize(Policy = AuthorizationPolicies.Operator)]
public class StationsController : ControllerBase
{
    private readonly AppDbContext _db;

    public StationsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<List<StationResponse>>> List(Guid tenantId)
    {
        var stations = await _db.Stations.Where(s => s.TenantId == tenantId).ToListAsync();
        return stations.Select(ToResponse).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<StationCreatedResponse>> Create(Guid tenantId, CreateStationRequest request)
    {
        var tenant = await _db.Tenants.FindAsync(tenantId);
        if (tenant is null) return NotFound();

        if (!Enum.TryParse<DeviceVendor>(request.DeviceVendor, out var vendor) || !Enum.IsDefined(vendor))
            return BadRequest($"Invalid device vendor '{request.DeviceVendor}'.");

        if (vendor != tenant.DeviceVendor)
            return BadRequest($"Station vendor must match tenant vendor ({tenant.DeviceVendor}).");

        var (plaintextKey, hash) = StationKeyGenerator.Generate();
        var station = new Station
        {
            TenantId = tenantId,
            Name = request.Name,
            DeviceVendor = vendor,
            ApiKeyHash = hash,
        };
        _db.Stations.Add(station);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(List), new { tenantId }, new StationCreatedResponse(station.Id, station.Name, plaintextKey));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid tenantId, Guid id)
    {
        var station = await _db.Stations.SingleOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId);
        if (station is null) return NotFound();

        _db.Stations.Remove(station);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static StationResponse ToResponse(Station s) =>
        new(s.Id, s.TenantId, s.Name, s.DeviceVendor.ToString(), s.LastSyncedAt);
}
