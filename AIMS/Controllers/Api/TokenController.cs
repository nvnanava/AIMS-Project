using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;

[Authorize] // 💡 Use OCID Cookie as the default Auth (since we need tokens somehow)
[ApiController]
[Route("api/[controller]")]
public class TokenController : ControllerBase
{
    // Inject ITokenAcquisition service
    private readonly ITokenAcquisition _tokenAcquisition;
    private readonly string _apiAudience;
    private readonly ILogger<TokenController> _logger;
    public TokenController(ITokenAcquisition tokenAcquisition, IConfiguration configuration, ILogger<TokenController> logger)
    {
        _tokenAcquisition = tokenAcquisition;
        _apiAudience = configuration["AzureAd:ApiAudience"]
            ?? throw new InvalidOperationException("Configuration error: 'AzureAd:ApiAudience' is missing or null.");
        _logger = logger;
    }
    [HttpGet]
    public async Task<IActionResult> GetToken()
    {
        try
        {
            var scopes = new[] { $"{_apiAudience}/access_as_user" };

            // Pass the 'User' ClaimsPrincipal explicitly.
            // resolves the 'ErrorCode: user_null' and 'No account or login hint' errors.
            var freshToken = await _tokenAcquisition.GetAccessTokenForUserAsync(
                scopes: scopes,
                user: this.User);

            if (string.IsNullOrEmpty(freshToken))
            {
                return Unauthorized(new { error = "Cannot acquire token silently. Re-login required." });
            }

            return Ok(new { access_token = freshToken });
        }
        catch (Exception ex) when (ex is MicrosoftIdentityWebChallengeUserException)
        {
            // Handle the specific MSAL challenge.
            // If MSAL still throws an MsalUiRequiredException, it means the refresh token 
            // has truly expired or needs re-consent. tell the frontend to re-authenticate.
            _logger.LogError(ex, "MsalUiRequiredException encountered. Forcing client re-login.");
            return Unauthorized(new { error = "MsalUiRequired: Re-authentication required." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FATAL ERROR acquiring access token for user.");
            return StatusCode(500, new { error = "Failed to get access token: 500" });
        }
    }
}