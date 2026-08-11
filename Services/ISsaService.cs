namespace ApsSamples.Services;

public interface ISsaService
{
    Task<string> CreateSsaAsync(string name);
    Task<List<string>> GetAllSsaIdsAsync();
    Task<string> StoreSsaKeyAsync(string ssaId);
    Task<string> GetSsaEmailAsync(string ssaId);
    Task<string> GetSsaTokenAsync(string ssaId, List<string> scopes);
    Task<string> AddSsaToProjectAsync(string projectId, string ssaEmail, string? companyId, List<string> roleIds, List<string> productKeys, string accessToken);
    Task RemoveSsaFromProjectAsync(string projectId, string projectUserId, string accessToken);
    Task DeleteSsaAsync(string ssaId);
}
