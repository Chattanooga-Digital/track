using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Track.Website.Api.v0.Controllers;

/// <summary>Public build metadata; anonymous so the version can show on the login screen too.</summary>
[ApiController]
[AllowAnonymous]
[Route("/api/v0/about")]
public class AboutController : ControllerBase
{
    private static readonly string AppVersion = ResolveVersion();

    [HttpGet]
    public IActionResult Get() => Ok(new { Version = AppVersion });

    private static string ResolveVersion()
    {
        var informational = Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(informational))
            return "unknown";

        var versionAndCommit = informational.Split('+', 2);
        // CI's fourth version component is its run number, which the commit already pins down.
        var release = string.Join('.', versionAndCommit[0].Split('.').Take(3));
        return versionAndCommit is [_, var commit] ? $"{release}+{commit[..Math.Min(7, commit.Length)]}" : release;
    }
}
