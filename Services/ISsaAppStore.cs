using ApsSamples.Models;

namespace ApsSamples.Services;

public interface ISsaAppStore
{
    // Oldest first, so the first-added app fills first.
    IReadOnlyList<SsaAppCredential> GetByProject(string projectId);
    SsaAppCredential? Get(string clientId);

    // False when the client ID is already registered in any project.
    Task<bool> TryAddAsync(SsaAppCredential app);
    Task<bool> UpdateSecretAsync(string clientId, string newSecret);
    Task<bool> RemoveAsync(string clientId);
}
