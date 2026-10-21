namespace ApsSamples.Services;

public interface IRevitWebhookTokenService
{
    Task SaveRefreshTokenAsync(string userId, string refreshToken);
    Task<bool> HasTokenAsync(string userId);

    // Returns a valid 3LO access token for the user, refreshing (and persisting the rotated
    // refresh token) when needed. Throws InvalidOperationException when the user must sign in again.
    Task<string> GetAccessTokenAsync(string userId, CancellationToken cancellationToken = default);
}
