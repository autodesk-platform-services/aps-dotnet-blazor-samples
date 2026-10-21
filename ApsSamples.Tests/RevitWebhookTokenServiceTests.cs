using ApsSamples.Services;
using Autodesk.Authentication.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ApsSamples.Tests;

[Collection("SerialFilesystem")]
public class RevitWebhookTokenServiceTests : IClassFixture<TempWorkingDirectoryFixture>
{
    private readonly TempWorkingDirectoryFixture _fixture;

    public RevitWebhookTokenServiceTests(TempWorkingDirectoryFixture fixture)
    {
        _fixture = fixture;
    }

    private static RevitWebhookTokenService NewService(FakeAuthService? auth = null)
    {
        var services = new ServiceCollection();
        services.AddScoped<IAPSAuthenticationService>(_ => auth ?? new FakeAuthService());
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        return new RevitWebhookTokenService(scopeFactory, NullLogger<RevitWebhookTokenService>.Instance);
    }

    private static string NewUserId() => Guid.NewGuid().ToString("N");

    [Fact]
    public async Task SaveRefreshTokenAsync_ThenHasToken_ReturnsTrue()
    {
        var svc = NewService();
        var userId = NewUserId();

        Assert.False(await svc.HasTokenAsync(userId));

        await svc.SaveRefreshTokenAsync(userId, "refresh-1");

        Assert.True(await svc.HasTokenAsync(userId));
    }

    [Fact]
    public async Task SaveRefreshTokenAsync_NewInstance_LoadsFromDisk()
    {
        var userId = NewUserId();
        await NewService().SaveRefreshTokenAsync(userId, "refresh-1");

        var auth = new FakeAuthService();
        var reloaded = NewService(auth);

        Assert.True(await reloaded.HasTokenAsync(userId));
        await reloaded.GetAccessTokenAsync(userId);
        Assert.Equal(["refresh-1"], auth.RefreshedWith);
    }

    [Fact]
    public async Task SaveRefreshTokenAsync_SecondSave_OverwritesToken()
    {
        var userId = NewUserId();
        var svc = NewService();
        await svc.SaveRefreshTokenAsync(userId, "refresh-1");
        await svc.SaveRefreshTokenAsync(userId, "refresh-2");

        var auth = new FakeAuthService();
        await NewService(auth).GetAccessTokenAsync(userId);

        Assert.Equal(["refresh-2"], auth.RefreshedWith);
    }

    [Fact]
    public async Task GetAccessTokenAsync_NoStoredToken_ThrowsSignInAgain()
    {
        var svc = NewService();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.GetAccessTokenAsync(NewUserId()));

        Assert.StartsWith("Sign in again", ex.Message);
    }

    [Fact]
    public async Task GetAccessTokenAsync_Refreshes_PersistsRotatedToken()
    {
        var userId = NewUserId();
        var auth = new FakeAuthService();
        var svc = NewService(auth);
        await svc.SaveRefreshTokenAsync(userId, "refresh-1");

        var accessToken = await svc.GetAccessTokenAsync(userId);

        Assert.Equal("access-for-refresh-1", accessToken);

        // A new instance (e.g. after a restart) must use the rotated token, not the original one.
        var reloadedAuth = new FakeAuthService();
        await NewService(reloadedAuth).GetAccessTokenAsync(userId);
        Assert.Equal(["rotated-refresh-1"], reloadedAuth.RefreshedWith);
    }

    [Fact]
    public async Task GetAccessTokenAsync_CalledTwice_UsesCachedAccessToken()
    {
        var userId = NewUserId();
        var auth = new FakeAuthService();
        var svc = NewService(auth);
        await svc.SaveRefreshTokenAsync(userId, "refresh-1");

        var first = await svc.GetAccessTokenAsync(userId);
        var second = await svc.GetAccessTokenAsync(userId);

        Assert.Equal(first, second);
        Assert.Single(auth.RefreshedWith);
    }

    [Fact]
    public async Task GetAccessTokenAsync_RefreshRejected_ThrowsSignInAgain()
    {
        var userId = NewUserId();
        var svc = NewService(new FakeAuthService { FailRefresh = true });
        await svc.SaveRefreshTokenAsync(userId, "refresh-1");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.GetAccessTokenAsync(userId));

        Assert.StartsWith("Sign in again", ex.Message);
    }

    private sealed class FakeAuthService : IAPSAuthenticationService
    {
        public List<string> RefreshedWith { get; } = [];
        public bool FailRefresh { get; init; }

        public string ClientId => "client-id";
        public string ClientSecret => "client-secret";
        public string? CallbackUrl => null;

        public Task<ThreeLeggedToken> RefreshThreeLeggedTokenAsync(string refreshToken)
        {
            RefreshedWith.Add(refreshToken);
            if (FailRefresh)
            {
                throw new HttpRequestException("invalid_grant");
            }

            return Task.FromResult(new ThreeLeggedToken
            {
                AccessToken = $"access-for-{refreshToken}",
                RefreshToken = $"rotated-{refreshToken}",
                ExpiresIn = 3600
            });
        }

        public Task<string> GetThreeLeggedTokenAsync(string code, List<Scopes> scopes) => throw new NotImplementedException();
        public Task<ThreeLeggedToken> ExchangeCodeAsync(string code, List<Scopes> scopes) => throw new NotImplementedException();
        public Task<string> RefreshTokenAsync(string refreshToken) => throw new NotImplementedException();
        public string GetAuthorizationUrl(List<Scopes> scopes) => throw new NotImplementedException();
        public Task<UserInfo> GetUserInfoAsync(string accessToken) => throw new NotImplementedException();
        public Task<string> GetTwoLeggedTokenAsync() => throw new NotImplementedException();
        public Task<string> GetSsaAppTwoLeggedTokenAsync() => throw new NotImplementedException();
    }
}
