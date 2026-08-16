using AttendanceApi.Auth;
using AttendanceApi.Data;
using AttendanceApi.Dtos;
using AttendanceApi.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceApi.Controllers;

[ApiController]
[Route("api/punches")]
[Authorize(AuthenticationSchemes = StationKeySchemes.Name)]
public class PunchesController : ControllerBase
{
    private readonly AppDbContext _db;

    public PunchesController(AppDbContext db) => _db = db;

    [HttpPost("batch")]
    public async Task<ActionResult<PunchBatchResponse>> Batch(PunchBatchRequest request)
    {
        var tenantId = User.TenantId()!.Value;
        var stationId = User.StationId()!.Value;

        // Validate the whole batch up front: the desktop agent needs an unambiguous
        // accept/reject signal for the entire request so it knows whether it's safe
        // to clear its local retry queue. A partially-invalid batch is rejected in
        // full rather than silently dropping just the bad punch.
        var parsedTypes = new Dictionary<Guid, PunchType>();
        var seenIds = new HashSet<Guid>();
        var employeeIds = new HashSet<Guid>();
        foreach (var dto in request.Punches)
        {
            if (!seenIds.Add(dto.Id))
                return BadRequest($"Duplicate punch id '{dto.Id}' within the same batch.");

            if (!Enum.TryParse<PunchType>(dto.PunchType, out var punchType) || !Enum.IsDefined(punchType))
                return BadRequest($"Invalid punch type '{dto.PunchType}'.");

            parsedTypes[dto.Id] = punchType;
            employeeIds.Add(dto.EmployeeId);
        }

        var validEmployeeIds = (await _db.Employees
            .Where(e => e.TenantId == tenantId && employeeIds.Contains(e.Id))
            .Select(e => e.Id)
            .ToListAsync()).ToHashSet();

        if (employeeIds.Any(id => !validEmployeeIds.Contains(id)))
        {
            var unknownEmployeeId = employeeIds.First(id => !validEmployeeIds.Contains(id));
            return BadRequest($"Employee '{unknownEmployeeId}' does not belong to this tenant.");
        }

        var existingIds = (await _db.Punches.Where(p => seenIds.Contains(p.Id)).Select(p => p.Id).ToListAsync()).ToHashSet();

        var accepted = new List<Guid>();
        foreach (var dto in request.Punches)
        {
            accepted.Add(dto.Id);
            if (existingIds.Contains(dto.Id)) continue;

            _db.Punches.Add(new Punch
            {
                Id = dto.Id,
                TenantId = tenantId,
                EmployeeId = dto.EmployeeId,
                StationId = stationId,
                PunchType = parsedTypes[dto.Id],
                Timestamp = dto.Timestamp,
            });
        }

        var station = await _db.Stations.SingleAsync(s => s.Id == stationId);
        station.LastSyncedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync();
        return new PunchBatchResponse(accepted);
    }
}
