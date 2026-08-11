using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Autodesk.Authentication;
using Autodesk.Authentication.Model;
using Autodesk.SDKManager;

namespace ApsSamples.Services;

public interface IAPSAuthenticationService
{
    string ClientId { get; }
    string ClientSecret { get; }
    string? CallbackUrl { get; }
    Task<string> GetThreeLeggedTokenAsync(string code, List<Scopes> scopes);
    Task<string> RefreshTokenAsync(string refreshToken);
    string GetAuthorizationUrl(List<Scopes> scopes);
    Task<UserInfo> GetUserInfoAsync(string accessToken);

    // App-level (2-legged) token, for server-side lifecycle work that isn't tied to any specific
    // user's session - e.g. managing webhooks from a background timeout or an incoming webhook
    // callback, where no user access token is available.
    Task<string> GetTwoLeggedTokenAsync();

    // App-level (2-legged) token minted with the *SSA App* credentials (SsaApp:ClientId/Secret),
    // scoped for SSA management endpoints. Distinct from the primary Forge app token above.
    Task<string> GetSsaAppTwoLeggedTokenAsync();
}

public class APSAuthenticationService : IAPSAuthenticationService
{
    private readonly AuthenticationClient _authClient;
    private readonly IConfiguration _configuration;
    private string? _cachedToken;
    private DateTime _tokenExpiration;
    private string? _twoLeggedToken;
    private DateTime _twoLeggedTokenExpiration;
    private string? _ssaAppToken;
    private DateTime _ssaAppTokenExpiration;

    public string ClientId { get; }
    public string ClientSecret { get; }
    public string? CallbackUrl { get; }

    public APSAuthenticationService(AuthenticationClient authClient, IConfiguration configuration, string clientId, string clientSecret, string? callbackUrl = null)
    {
        ClientId = clientId ?? throw new ArgumentNullException(nameof(clientId));
        ClientSecret = clientSecret ?? throw new ArgumentNullException(nameof(clientSecret));
        CallbackUrl = callbackUrl;
        _authClient = authClient;
        _configuration = configuration;
        _tokenExpiration = DateTime.MinValue;
    }

    public string GetAuthorizationUrl(List<Scopes> scopes)
    {
        if (string.IsNullOrEmpty(CallbackUrl))
        {
            throw new InvalidOperationException("CallbackUrl must be configured for three-legged authentication");
        }

        return _authClient.Authorize(ClientId, ResponseType.Code, CallbackUrl, scopes);
    }

    public async Task<string> GetThreeLeggedTokenAsync(string code, List<Scopes> scopes)
    {
        if (string.IsNullOrEmpty(CallbackUrl))
        {
            throw new InvalidOperationException("CallbackUrl must be configured for three-legged authentication");
        }

        var auth = await _authClient.GetThreeLeggedTokenAsync(ClientId, code, CallbackUrl, clientSecret: ClientSecret);
        return auth.AccessToken;
    }

    public async Task<string> RefreshTokenAsync(string refreshToken)
    {
        var auth = await _authClient.RefreshTokenAsync(ClientId, refreshToken);
        _cachedToken = auth.AccessToken;
        _tokenExpiration = DateTime.UtcNow.AddSeconds((auth.ExpiresIn ?? 3600) - 60);

        return _cachedToken;
    }

    public async Task<UserInfo> GetUserInfoAsync(string accessToken)
    {
        return await _authClient.GetUserInfoAsync(accessToken);
    }

    public async Task<string> GetTwoLeggedTokenAsync()
    {
        if (_twoLeggedToken != null && DateTime.UtcNow < _twoLeggedTokenExpiration)
        {
            return _twoLeggedToken;
        }

        var token = await _authClient.GetTwoLeggedTokenAsync(
            ClientId,
            ClientSecret,
            new List<Scopes> { Scopes.DataRead, Scopes.DataWrite });

        _twoLeggedToken = token.AccessToken;
        _twoLeggedTokenExpiration = DateTime.UtcNow.AddSeconds((token.ExpiresIn ?? 3600) - 60);

        return _twoLeggedToken;
    }

    // The Autodesk.Authentication 2.0.1 Scopes enum doesn't yet expose the
    // application:service_account:* scopes needed to manage SSAs, so mint the SSA App token
    // by POSTing to the v2 token endpoint directly.
    public async Task<string> GetSsaAppTwoLeggedTokenAsync()
    {
        if (_ssaAppToken != null && DateTime.UtcNow < _ssaAppTokenExpiration)
        {
            return _ssaAppToken;
        }

        var ssaClientId = _configuration["SsaApp:ClientId"]
            ?? throw new InvalidOperationException("SsaApp:ClientId is required for SSA management operations");
        var ssaClientSecret = _configuration["SsaApp:ClientSecret"]
            ?? throw new InvalidOperationException("SsaApp:ClientSecret is required for SSA management operations");

        const string scope = "application:service_account:read application:service_account:write "
            + "application:service_account_key:read application:service_account_key:write";

        using var http = new HttpClient();
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ssaClientId}:{ssaClientSecret}"));
        var request = new HttpRequestMessage(HttpMethod.Post, "https://developer.api.autodesk.com/authentication/v2/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["scope"] = scope,
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

        using var response = await http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"SSA App 2LO token request failed ({(int)response.StatusCode}): {body}");
        }

        using var doc = JsonDocument.Parse(body);
        _ssaAppToken = doc.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("SSA App 2LO response missing access_token");
        var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600;
        _ssaAppTokenExpiration = DateTime.UtcNow.AddSeconds(expiresIn - 60);

        return _ssaAppToken;
    }
}
