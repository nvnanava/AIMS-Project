using AIMS.Utilities; // TestAuthHandler
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AIMS.Controllers.Auth;

public class AuthenticationController : Controller
{
    private readonly IOptionsMonitor<AuthenticationOptions> _authOptions;

    public AuthenticationController(IOptionsMonitor<AuthenticationOptions> authOptions)
    {
        _authOptions = authOptions;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Logout()
    {
        // Detect the active default scheme at runtime
        var defaultScheme = _authOptions.CurrentValue.DefaultScheme ?? string.Empty;

        // If we're running with TestAuth, there is no cookie/oidc sign-out handler.
        // Just bounce back to Home (tests don't need a federated sign-out).
        if (string.Equals(defaultScheme, TestAuthHandler.Scheme, StringComparison.Ordinal))
        {
            return RedirectToAction("Index", "Home");
        }

        // Normal dev/prod path: sign out of Cookies + OIDC (Entra)
        return SignOut(
            new AuthenticationProperties { RedirectUri = Url.Action("Index", "Home") },
            CookieAuthenticationDefaults.AuthenticationScheme,
            OpenIdConnectDefaults.AuthenticationScheme
        );
    }
}
