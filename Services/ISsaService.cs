namespace ApsSamples.Services;

public interface ISsaService
{
    /// <summary>
    /// Creates an SSA in the first of the project's APS apps that has room for one, and returns the
    /// owning app's client ID with the new SSA ID. Throws <see cref="InvalidOperationException"/>
    /// when every app is full or the project has none.
    /// </summary>
    Task<(string ClientId, string SsaId)> CreateSsaAsync(string projectId, string name);
    Task<List<string>> GetAllSsaIdsAsync(string clientId);
    Task<int> GetSsaCountAsync(string clientId);
    Task<string> StoreSsaKeyAsync(string clientId, string ssaId);
    Task<string> GetSsaEmailAsync(string clientId, string ssaId);

    /// <summary>Mints a token as the SSA, through the APS app recorded on its stored key.</summary>
    Task<string> GetSsaTokenAsync(string ssaId, List<string> scopes);
    Task<string> AddSsaToProjectAsync(string projectId, string ssaEmail, string? companyId, List<string> roleIds, List<string> productKeys, string accessToken);
    /// <param name="userId">The ACC project user ID or the user's Autodesk ID (an SSA's ID is its Autodesk ID).</param>
    Task RemoveSsaFromProjectAsync(string projectId, string userId, string accessToken);

    /// <summary>
    /// Deletes the SSA through its owning app and removes its stored key. With a blank
    /// <paramref name="clientId"/> (an SSA not linked to any app), only the stored key is removed.
    /// </summary>
    Task DeleteSsaAsync(string clientId, string ssaId);
    /// <summary>
    /// Removes the SSA's robot user from the project, then deletes the SSA. Uses <paramref name="projectUserId"/>
    /// when set, otherwise the SSA ID. The SSA is not deleted if removal fails.
    /// </summary>
    Task DeprovisionSsaAsync(string projectId, string clientId, string ssaId, string? projectUserId, string accessToken);

    /// <summary>
    /// Links SSAs created before per-project apps existed to the app that owns them: stored keys
    /// and the project's agents with no client ID get <paramref name="clientId"/> when the app
    /// lists their SSA. Returns the number of agents updated.
    /// </summary>
    Task<int> AdoptLegacySsasAsync(string projectId, string clientId);
}
