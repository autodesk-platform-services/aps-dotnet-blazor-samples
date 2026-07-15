using Autodesk.Authentication.Model;
using ApsSamples.Services;
using Microsoft.AspNetCore.Mvc;

namespace ApsSamples.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    IAPSAuthenticationService apsAuth,
    IUserSessionService userSession,
    ILogger<AuthController> logger) : ControllerBase
{
    [HttpGet("login")]
    public IActionResult Login([FromQuery] string? returnUrl = null)
    {
        var scopes = new List<Scopes>
        {
            Scopes.DataRead,
            Scopes.DataWrite,
            Scopes.DataCreate,
            Scopes.CodeAll,
            Scopes.ViewablesRead
        };

        // Store return URL in session or state parameter
        if (!string.IsNullOrEmpty(returnUrl))
        {
            HttpContext.Session.SetString("ReturnUrl", returnUrl);
        }

        var authUrl = apsAuth.GetAuthorizationUrl(scopes);
        return Redirect(authUrl);
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? error, [FromQuery] string? state)
    {
        try
        {
            if (!string.IsNullOrEmpty(error))
            {
                logger.LogError("OAuth error: {Error}", error);
                return Redirect($"/?error={Uri.EscapeDataString(error)}");
            }

            if (string.IsNullOrEmpty(code))
            {
                logger.LogError("No authorization code received");
                return Redirect("/?error=no_code");
            }

            // Exchange code for token
            var scopes = new List<Scopes>
            {
                Scopes.DataRead,
                Scopes.DataWrite,
                Scopes.DataCreate,
                Scopes.UserRead,
                Scopes.CodeAll,
                Scopes.ViewablesRead
            };

            var token = await apsAuth.GetThreeLeggedTokenAsync(code, scopes);
            
            // Store token in session via UserSessionService
            userSession.AccessToken = token;
            
            // Get user information
            try
            {
                var userInfo = await apsAuth.GetUserInfoAsync(token);
                userSession.UserName = userInfo.Name;
                userSession.UserEmail = userInfo.Email;
                userSession.UserId = userInfo.Sub;
                
                logger.LogInformation("User authenticated: {UserName} ({Email})", userInfo.Name, userInfo.Email);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not retrieve user info, but authentication succeeded");
            }
            
            // Ensure session is saved
            await HttpContext.Session.CommitAsync();

            logger.LogInformation("Successfully authenticated user");

            // Get return URL from session
            var returnUrl = HttpContext.Session.GetString("ReturnUrl") ?? "/";
            HttpContext.Session.Remove("ReturnUrl");

            return Redirect(returnUrl);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during OAuth callback");
            return Redirect($"/?error={Uri.EscapeDataString(ex.Message)}");
        }
    }

    [HttpGet("viewer-token")]
    public IActionResult ViewerToken()
    {
        if (string.IsNullOrEmpty(userSession.AccessToken))
        {
            return Unauthorized();
        }

        return Ok(new { accessToken = userSession.AccessToken, expiresIn = 3600 });
    }

    [HttpGet("logout")]
    public IActionResult Logout()
    {
        userSession.ClearSession();
        HttpContext.Session.Clear();
        return Redirect("/");
    }
}
