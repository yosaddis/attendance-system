using AttendanceApi.Auth;
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
    public IActionResult GetForStation() => Ok(new { status = "ok", stationId = User.StationId() });
}
