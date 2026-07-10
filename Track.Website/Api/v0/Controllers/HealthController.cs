using Microsoft.AspNetCore.Mvc;

namespace Track.Website.Api.v0.Controllers;

[ApiController]
[Route("/api/v0/health")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new { Status = "ok", Timestamp = DateTime.UtcNow });
    }
}
