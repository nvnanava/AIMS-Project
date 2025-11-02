//using Microsoft.Identity.Web;
//using AIMS.Helpers; // For ClaimsPrincipalExtensions
using AIMS.Controllers.Auth;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

public class AuthenticationControllerTests
{
    [Fact]

    public void Logout_Test()
    {
        // Arrange
        var controller = new AuthenticationController();
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        // Use a small concrete IUrlHelper to avoid mocking extension methods
        controller.Url = new TestUrlHelper();

        // Act
        var result = controller.Logout() as SignOutResult;

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.AuthenticationSchemes.Count());
        Assert.Contains(CookieAuthenticationDefaults.AuthenticationScheme, result.AuthenticationSchemes);
        Assert.Contains(OpenIdConnectDefaults.AuthenticationScheme, result.AuthenticationSchemes);
        var authProperties = result.Properties;
        Assert.NotNull(authProperties);
        Assert.Equal("/Home/Index", result!.Properties!.RedirectUri);
    }
}

// Small test helper that implements IUrlHelper and returns a fixed Action URL.
internal class TestUrlHelper : IUrlHelper
{
    public ActionContext ActionContext { get; set; } = new ActionContext();

    // Use fully-qualified nullable parameter types to match the current IUrlHelper interface
    public string? Action(Microsoft.AspNetCore.Mvc.Routing.UrlActionContext? actionContext)
    {
        return "/Home/Index";
    }

    public string? Content(string? contentPath)
    {
        throw new NotImplementedException();
    }

    public bool IsLocalUrl(string? url)
    {
        // Simple heuristic; not used in tests
        return url != null && (url.StartsWith("/") || url.StartsWith("~"));
    }

    public string? Link(string? routeName, object? values)
    {
        throw new NotImplementedException();
    }

    public string? RouteUrl(Microsoft.AspNetCore.Mvc.Routing.UrlRouteContext? routeContext)
    {
        throw new NotImplementedException();
    }
}

