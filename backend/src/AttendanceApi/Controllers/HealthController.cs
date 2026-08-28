using AttendanceApi.Auth;
using AttendanceApi.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AttendanceApi.Controllers;

[ApiController]
[Route("api/health")]
public class HealthController : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Get() => Ok(new { status = "ok" });

    [HttpGet("station")]
    [Authorize(AuthenticationSchemes = StationKeySchemes.Name)]
    public ActionResult<StationHealthResponse> GetForStation() =>
        Ok(new StationHealthResponse("ok", User.StationId()!.Value, User.TenantId()!.Value));
}
