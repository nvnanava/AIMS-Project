using System.Linq;
using AIMS.Controllers.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Xunit;

public class AuthenticationControllerTests
{
    [Fact]
    public void Logout_Test()
    {
        // Arrange: provide AuthenticationOptions via a fake monitor
        var authOptions = new AuthenticationOptions
        {
            DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme,
            DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme,
            DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme
        };
        var monitor = new FakeOptionsMonitor<AuthenticationOptions>(authOptions);

        var controller = new AuthenticationController(monitor)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            Url = new TestUrlHelper()
        };

        // Act
        var result = controller.Logout() as SignOutResult;

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result!.AuthenticationSchemes.Count());
        Assert.Contains(CookieAuthenticationDefaults.AuthenticationScheme, result.AuthenticationSchemes);
        Assert.Contains(OpenIdConnectDefaults.AuthenticationScheme, result.AuthenticationSchemes);
        Assert.Equal("/Home/Index", result.Properties!.RedirectUri);
    }
}

// Minimal options monitor for tests
internal sealed class FakeOptionsMonitor<T> : IOptionsMonitor<T>
{
    public FakeOptionsMonitor(T current) => CurrentValue = current;
    public T CurrentValue { get; }
    public T Get(string? name) => CurrentValue;
    public IDisposable OnChange(Action<T, string> listener) => new Noop();
    private sealed class Noop : IDisposable { public void Dispose() { } }
}

// Small test helper that implements IUrlHelper and returns a fixed Action URL.
internal class TestUrlHelper : IUrlHelper
{
    public ActionContext ActionContext { get; set; } = new ActionContext();
    public string? Action(Microsoft.AspNetCore.Mvc.Routing.UrlActionContext? actionContext) => "/Home/Index";
    public string? Content(string? contentPath) => throw new NotImplementedException();
    public bool IsLocalUrl(string? url) => url != null && (url.StartsWith("/") || url.StartsWith("~"));
    public string? Link(string? routeName, object? values) => throw new NotImplementedException();
    public string? RouteUrl(Microsoft.AspNetCore.Mvc.Routing.UrlRouteContext? routeContext) => throw new NotImplementedException();
}
