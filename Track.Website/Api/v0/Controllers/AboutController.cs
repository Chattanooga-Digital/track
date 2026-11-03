using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Track.Website.Api.v0.Controllers;

/// <summary>Public build metadata; anonymous so the version can show on the login screen too.</summary>
[ApiController]
[AllowAnonymous]
[Route("/api/v0/about")]
public class AboutController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { Version = AppVersion.Display });
}
