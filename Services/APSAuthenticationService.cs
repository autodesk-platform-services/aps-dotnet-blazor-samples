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
}

public class APSAuthenticationService : IAPSAuthenticationService
{
    private readonly AuthenticationClient _authClient;
    private string? _cachedToken;
    private DateTime _tokenExpiration;

    public string ClientId { get; }
    public string ClientSecret { get; }
    public string? CallbackUrl { get; }

    public APSAuthenticationService(string clientId, string clientSecret, string? callbackUrl = null)
    {
        ClientId = clientId ?? throw new ArgumentNullException(nameof(clientId));
        ClientSecret = clientSecret ?? throw new ArgumentNullException(nameof(clientSecret));
        CallbackUrl = callbackUrl;
        var sdkManager = SdkManagerBuilder.Create().Build();
        _authClient = new AuthenticationClient(sdkManager);
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
}
