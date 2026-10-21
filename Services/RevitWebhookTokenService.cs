using System.Collections.Concurrent;
using System.Text.Json;
using ApsSamples.Models;
using Autodesk.Authentication.Model;

namespace ApsSamples.Services;

// Keeps one refresh-token chain per user (not per subscription): APS refresh tokens rotate on use,
// so two copies of the same token would invalidate each other. Refreshes are serialized per user
// and the rotated token is persisted immediately.
// Tokens are stored in plaintext in Data/revit-webhook-tokens.json, like Data/ssa-keys.json.
public class RevitWebhookTokenService : IRevitWebhookTokenService
{
    // Refresh the access token a little before it actually expires so callers never get one that
    // dies mid-request.
    private static readonly TimeSpan ExpirySafetyMargin = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<string, RevitWebhookUserToken> _tokens = new();
    private readonly ConcurrentDictionary<string, (string AccessToken, DateTime ExpiresAt)> _accessTokens = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _userLocks = new();
    // IAPSAuthenticationService is Scoped, but this service is a Singleton, so a scope is created
    // on demand each time a refresh is needed instead of injecting it directly.
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RevitWebhookTokenService> _logger;
    private readonly string _filePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public RevitWebhookTokenService(IServiceScopeFactory scopeFactory, ILogger<RevitWebhookTokenService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        var dataDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Data");
        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, "revit-webhook-tokens.json");

        LoadFromFile();
    }

    public async Task SaveRefreshTokenAsync(string userId, string refreshToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        ArgumentException.ThrowIfNullOrEmpty(refreshToken);

        var userLock = GetUserLock(userId);
        await userLock.WaitAsync();
        try
        {
            _tokens[userId] = new RevitWebhookUserToken
            {
                UserId = userId,
                RefreshToken = refreshToken,
                UpdatedAt = DateTime.UtcNow
            };
            _accessTokens.TryRemove(userId, out _);
            await SaveToFileAsync();
        }
        finally
        {
            userLock.Release();
        }

        _logger.LogInformation("Stored Revit webhook refresh token for user {UserId}", userId);
    }

    public Task<bool> HasTokenAsync(string userId)
    {
        var hasToken = !string.IsNullOrEmpty(userId)
            && _tokens.TryGetValue(userId, out var token)
            && !string.IsNullOrEmpty(token.RefreshToken);

        return Task.FromResult(hasToken);
    }

    public async Task<string> GetAccessTokenAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (TryGetCachedAccessToken(userId, out var cached))
        {
            return cached;
        }

        var userLock = GetUserLock(userId);
        await userLock.WaitAsync(cancellationToken);
        try
        {
            // Another caller may have refreshed while we were waiting for the lock.
            if (TryGetCachedAccessToken(userId, out cached))
            {
                return cached;
            }

            if (!_tokens.TryGetValue(userId, out var stored) || string.IsNullOrEmpty(stored.RefreshToken))
            {
                throw new InvalidOperationException(
                    "Sign in again: no stored refresh token is available for this user.");
            }

            ThreeLeggedToken refreshed;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var apsAuth = scope.ServiceProvider.GetRequiredService<IAPSAuthenticationService>();
                refreshed = await apsAuth.RefreshThreeLeggedTokenAsync(stored.RefreshToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Refreshing the Revit webhook token failed for user {UserId}", userId);
                throw new InvalidOperationException(
                    "Sign in again: the stored refresh token was rejected or could not be refreshed.", ex);
            }

            if (!string.IsNullOrEmpty(refreshed.RefreshToken))
            {
                _tokens[userId] = new RevitWebhookUserToken
                {
                    UserId = userId,
                    RefreshToken = refreshed.RefreshToken,
                    UpdatedAt = DateTime.UtcNow
                };
                await SaveToFileAsync();
            }

            var expiresAt = DateTime.UtcNow.AddSeconds(refreshed.ExpiresIn ?? 3600) - ExpirySafetyMargin;
            _accessTokens[userId] = (refreshed.AccessToken, expiresAt);

            return refreshed.AccessToken;
        }
        finally
        {
            userLock.Release();
        }
    }

    private bool TryGetCachedAccessToken(string userId, out string accessToken)
    {
        if (_accessTokens.TryGetValue(userId, out var cached) && DateTime.UtcNow < cached.ExpiresAt)
        {
            accessToken = cached.AccessToken;
            return true;
        }

        accessToken = string.Empty;
        return false;
    }

    private SemaphoreSlim GetUserLock(string userId) => _userLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));

    private async Task SaveToFileAsync()
    {
        await _fileLock.WaitAsync();
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var json = JsonSerializer.Serialize(_tokens.Values.ToList(), options);
            await File.WriteAllTextAsync(_filePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving Revit webhook tokens to file");
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private void LoadFromFile()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                _logger.LogInformation("No existing Revit webhook tokens file found at {Path}", _filePath);
                return;
            }

            var json = File.ReadAllText(_filePath);
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var loaded = JsonSerializer.Deserialize<List<RevitWebhookUserToken>>(json, options);
            if (loaded != null)
            {
                foreach (var item in loaded.Where(t => !string.IsNullOrEmpty(t.UserId)))
                {
                    _tokens[item.UserId] = item;
                }
                _logger.LogInformation("Loaded {Count} Revit webhook tokens from {Path}", _tokens.Count, _filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading Revit webhook tokens from file");
        }
    }
}
