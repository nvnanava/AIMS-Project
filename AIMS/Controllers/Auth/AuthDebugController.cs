using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIMS.Controllers.Auth;

[ApiController]
[Route("api/auth")]
public class AuthDebugController : ControllerBase
{
    [HttpGet("me")]
    [Authorize]
    public IActionResult Me()
    {
        var claims = User.Claims.Select(c => new { c.Type, c.Value }).ToList();
        var name = User.FindFirst("preferred_username")?.Value
                    ?? User.Identity?.Name ?? "(none)";
        return Ok(new
        {
            Authenticated = User.Identity?.IsAuthenticated ?? false,
            Name = name,
            Claims = claims
        });
    }
}
