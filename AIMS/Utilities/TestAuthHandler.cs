using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIMS.Utilities;

public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public new const string Scheme = "TestAuth";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Check if real authentication is requested
        var req = Context.Request;
        var cookies = Context.Request.Cookies;

        // If user wants real auth (via query param or cookie), skip test auth
        var useRealAuth = req.Query["realauth"].FirstOrDefault() == "true" ||
                         cookies.TryGetValue("use_real_auth", out var realAuthCookie) && realAuthCookie == "true";

        if (useRealAuth)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var cookieOid = cookies.TryGetValue("imp_oid", out var coid) ? coid : null;
        var cookieUpn = cookies.TryGetValue("imp_upn", out var cupn) ? cupn : null;

        var qOid = req.Query["oid"].FirstOrDefault();
        var qUpn = req.Query["upn"].FirstOrDefault();


        var oid = !string.IsNullOrWhiteSpace(qOid) ? qOid
                 : !string.IsNullOrWhiteSpace(cookieOid) ? cookieOid
                 : "test-user"; // last-resort default (you can replace this)

        var upn = !string.IsNullOrWhiteSpace(qUpn) ? qUpn
                 : !string.IsNullOrWhiteSpace(cookieUpn) ? cookieUpn
                 : "tburguillos@csus.edu"; // last-resort default

        // If query was used, persist it to cookies so reloads keep the identity
        if (!string.IsNullOrWhiteSpace(qOid) || !string.IsNullOrWhiteSpace(qUpn))
        {
            var opts = new CookieOptions { Expires = DateTimeOffset.UtcNow.AddDays(7), HttpOnly = false, Secure = false, IsEssential = true, SameSite = SameSiteMode.Lax };
            if (!string.IsNullOrWhiteSpace(qOid)) Context.Response.Cookies.Append("imp_oid", oid, opts);
            if (!string.IsNullOrWhiteSpace(qUpn)) Context.Response.Cookies.Append("imp_upn", upn, opts);
        }

        // If realauth=true query param is present, set the cookie and skip test auth
        if (req.Query["realauth"].FirstOrDefault() == "true")
        {
            var realAuthOpts = new CookieOptions { Expires = DateTimeOffset.UtcNow.AddDays(7), HttpOnly = false, Secure = false, IsEssential = true, SameSite = SameSiteMode.Lax };
            Context.Response.Cookies.Append("use_real_auth", "true", realAuthOpts);
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        // Build claims
        var claims = new[]
        {
    new Claim("oid", oid),
    new Claim("preferred_username", upn),
    new Claim(ClaimTypes.Name, upn),
    new Claim(ClaimTypes.NameIdentifier, oid)
    // new Claim("roles","Admin")
};

        var identity = new ClaimsIdentity(claims, Scheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
