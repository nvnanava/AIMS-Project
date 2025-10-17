using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;

namespace AIMS.Controllers.Auth;

public class AuthenticationController : Controller
{
    // Handle logout by redirecting to the appropriate authentication schemes
    [HttpPost]
    [ValidateAntiForgeryToken] // CSRF protection
    public IActionResult Logout() //function to logout the user
    {
        return SignOut(new AuthenticationProperties // Redirect to Entra SSO after logout
        {
            RedirectUri = Url.Action("Index", "Home")
        },
        CookieAuthenticationDefaults.AuthenticationScheme, // Sign out of cookies
        OpenIdConnectDefaults.AuthenticationScheme); // Sign out of OpenID Connect (Entra)
    }
}
