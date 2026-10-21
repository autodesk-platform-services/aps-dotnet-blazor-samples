namespace ApsSamples.Services;

public interface ISsaService
{
    Task<string> CreateSsaAsync(string name);
    Task<List<string>> GetAllSsaIdsAsync();
    Task<string> StoreSsaKeyAsync(string ssaId);
    Task<string> GetSsaEmailAsync(string ssaId);
    Task<string> GetSsaTokenAsync(string ssaId, List<string> scopes);
    Task<string> AddSsaToProjectAsync(string projectId, string ssaEmail, string? companyId, List<string> roleIds, List<string> productKeys, string accessToken);
    /// <param name="userId">The ACC project user ID or the user's Autodesk ID (an SSA's ID is its Autodesk ID).</param>
    Task RemoveSsaFromProjectAsync(string projectId, string userId, string accessToken);
    Task DeleteSsaAsync(string ssaId);
    /// <summary>
    /// Removes the SSA's robot user from the project, then deletes the SSA. Uses <paramref name="projectUserId"/>
    /// when set, otherwise the SSA ID. The SSA is not deleted if removal fails.
    /// </summary>
    Task DeprovisionSsaAsync(string projectId, string ssaId, string? projectUserId, string accessToken);
}
