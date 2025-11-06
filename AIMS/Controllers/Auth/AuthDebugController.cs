using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
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
            Claims = claims,
            AuthenticationType = User.Identity?.AuthenticationType
        });
    }

    /// <summary>
    /// Switch to real Entra authentication (only available in development)
    /// </summary>
    [HttpGet("use-real-auth")]
    [AllowAnonymous]
    public IActionResult UseRealAuth()
    {
        if (!HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment())
        {
            return NotFound();
        }

        // Set cookie to enable real auth
        var cookieOptions = new CookieOptions 
        { 
            Expires = DateTimeOffset.UtcNow.AddDays(7), 
            HttpOnly = false, 
            Secure = false, 
            IsEssential = true, 
            SameSite = SameSiteMode.Lax 
        };
        
        Response.Cookies.Append("use_real_auth", "true", cookieOptions);
        
        // Clear any test auth cookies
        Response.Cookies.Delete("imp_oid");
        Response.Cookies.Delete("imp_upn");
        
        // Trigger authentication challenge to redirect to Entra
        return Challenge(new AuthenticationProperties
        {
            RedirectUri = Url.Action("Index", "Home")
        }, OpenIdConnectDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Switch back to test authentication (only available in development)
    /// </summary>
    [HttpGet("use-test-auth")]
    [AllowAnonymous]
    public IActionResult UseTestAuth()
    {
        if (!HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment())
        {
            return NotFound();
        }

        // Clear the real auth cookie
        Response.Cookies.Delete("use_real_auth");
        
        // Redirect to home to use test auth
        return Redirect(Url.Action("Index", "Home") ?? "/");
    }
}
