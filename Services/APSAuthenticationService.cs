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
}

public class APSAuthenticationService : IAPSAuthenticationService
{
    private readonly AuthenticationClient _authClient;
    private string? _cachedToken;
    private DateTime _tokenExpiration;
    private string? _twoLeggedToken;
    private DateTime _twoLeggedTokenExpiration;

    public string ClientId { get; }
    public string ClientSecret { get; }
    public string? CallbackUrl { get; }

    public APSAuthenticationService(AuthenticationClient authClient, string clientId, string clientSecret, string? callbackUrl = null)
    {
        ClientId = clientId ?? throw new ArgumentNullException(nameof(clientId));
        ClientSecret = clientSecret ?? throw new ArgumentNullException(nameof(clientSecret));
        CallbackUrl = callbackUrl;
        _authClient = authClient;
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
}
