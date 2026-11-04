using ApsSamples.Models;
using ApsSamples.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ApsSamples.Tests;

[Collection("SerialFilesystem")]
public class SsaAppStoreTests : IClassFixture<TempWorkingDirectoryFixture>
{
    private readonly TempWorkingDirectoryFixture _fixture;

    public SsaAppStoreTests(TempWorkingDirectoryFixture fixture)
    {
        _fixture = fixture;
    }

    private static SsaAppStore NewStore() => new(NullLogger<SsaAppStore>.Instance);

    // Tests share one data directory, so each uses its own project and client IDs.
    private static string NewProjectId() => "b." + Guid.NewGuid().ToString("N");

    private static SsaAppCredential NewApp(string projectId, DateTime? createdAt = null) => new()
    {
        ProjectId = projectId,
        ClientId = Guid.NewGuid().ToString("N"),
        ClientSecret = "s3cret",
        DisplayName = "Assistants app",
        CreatedAt = createdAt ?? DateTime.UtcNow
    };

    [Fact]
    public async Task TryAddAsync_NewInstance_LoadsAppFromDisk()
    {
        var app = NewApp(NewProjectId());
        Assert.True(await NewStore().TryAddAsync(app));

        var reloaded = NewStore().Get(app.ClientId);

        Assert.NotNull(reloaded);
        Assert.Equal(app.ProjectId, reloaded.ProjectId);
        Assert.Equal("s3cret", reloaded.ClientSecret);
        Assert.Equal("Assistants app", reloaded.DisplayName);
        Assert.Equal(app.CreatedAt, reloaded.CreatedAt);
        Assert.True(File.Exists(Path.Combine(_fixture.TempPath, "Data", "ssa-apps.json")));
    }

    [Fact]
    public async Task Get_IsCaseInsensitive()
    {
        var store = NewStore();
        var app = NewApp(NewProjectId());
        await store.TryAddAsync(app);

        Assert.Same(app, store.Get(app.ClientId.ToUpperInvariant()));
    }

    [Fact]
    public async Task GetByProject_ReturnsOnlyThatProjectsAppsOldestFirst()
    {
        var store = NewStore();
        var projectId = NewProjectId();
        var start = DateTime.UtcNow;
        var newer = NewApp(projectId, start.AddMinutes(5));
        var older = NewApp(projectId, start);
        var otherProject = NewApp(NewProjectId(), start.AddMinutes(-5));
        await store.TryAddAsync(newer);
        await store.TryAddAsync(older);
        await store.TryAddAsync(otherProject);

        Assert.Equal(
            [older.ClientId, newer.ClientId],
            store.GetByProject(projectId).Select(a => a.ClientId));
        Assert.Equal(
            [older.ClientId, newer.ClientId],
            NewStore().GetByProject(projectId).Select(a => a.ClientId));
    }

    [Fact]
    public async Task TryAddAsync_ClientIdRegisteredInAnotherProject_ReturnsFalse()
    {
        var store = NewStore();
        var app = NewApp(NewProjectId());
        await store.TryAddAsync(app);

        var duplicate = NewApp(NewProjectId());
        duplicate.ClientId = app.ClientId.ToUpperInvariant();

        Assert.False(await store.TryAddAsync(duplicate));
        Assert.Empty(store.GetByProject(duplicate.ProjectId));
        Assert.Equal(app.ProjectId, NewStore().Get(app.ClientId)!.ProjectId);
    }

    [Fact]
    public async Task UpdateSecretAsync_PersistsAcrossInstances()
    {
        var store = NewStore();
        var app = NewApp(NewProjectId());
        await store.TryAddAsync(app);

        Assert.True(await store.UpdateSecretAsync(app.ClientId, "rotated"));

        Assert.Equal("rotated", store.Get(app.ClientId)!.ClientSecret);
        Assert.Equal("rotated", NewStore().Get(app.ClientId)!.ClientSecret);
    }

    [Fact]
    public async Task RemoveAsync_RemovesFromStoreAndDisk()
    {
        var store = NewStore();
        var app = NewApp(NewProjectId());
        await store.TryAddAsync(app);

        Assert.True(await store.RemoveAsync(app.ClientId));
        Assert.False(await store.RemoveAsync(app.ClientId));

        Assert.Null(store.Get(app.ClientId));
        Assert.Null(NewStore().Get(app.ClientId));
        Assert.Empty(NewStore().GetByProject(app.ProjectId));
    }

    [Fact]
    public async Task UnknownClientId_ReturnsFalseOrNull()
    {
        var store = NewStore();

        Assert.Null(store.Get("unknown-client"));
        Assert.False(await store.UpdateSecretAsync("unknown-client", "secret"));
        Assert.False(await store.RemoveAsync("unknown-client"));
    }

    [Theory]
    [InlineData("abcdefghijkl", "abcd…ijkl")]
    [InlineData("abcdefgh", "abcdefgh")]
    [InlineData("", "")]
    public void MaskedClientId_ShowsFirstAndLastFourCharacters(string clientId, string expected)
    {
        Assert.Equal(expected, new SsaAppCredential { ClientId = clientId }.MaskedClientId);
    }

    [Fact]
    public async Task SavedFile_DoesNotContainMaskedClientId()
    {
        var app = NewApp(NewProjectId());
        await NewStore().TryAddAsync(app);

        var json = await File.ReadAllTextAsync(Path.Combine(_fixture.TempPath, "Data", "ssa-apps.json"));

        Assert.Contains("\"clientSecret\"", json);
        Assert.DoesNotContain("maskedClientId", json, StringComparison.OrdinalIgnoreCase);
    }
}
