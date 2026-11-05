using System.Text.Json;

namespace ApsSamples.Services;

public interface IAecDataModelService
{
    // AEC DM project whose alternativeIdentifiers.dataManagementAPIProjectId matches the Data Management
    // project id (with or without the "b." prefix), searched across every hub the user can see.
    Task<AecProject?> FindProjectAsync(string accessToken, string dataManagementProjectId, string? region, CancellationToken cancellationToken = default);

    // Element group of the AEC DM project whose alternativeIdentifiers.fileUrn matches the Data Management
    // lineage URN; null until the model has been extracted.
    Task<AecElementGroup?> FindElementGroupAsync(string accessToken, string aecProjectId, string lineageUrn, string? region, CancellationToken cancellationToken = default);

    // Ids of the RVT Links elements (Types and Instances) in the tip version of the element group.
    Task<IReadOnlyList<string>> GetLinkElementIdsAsync(string accessToken, string elementGroupId, string? region, CancellationToken cancellationToken = default);

    // All pages of diffElementGroupByVersionWithLatest (ADDITION and REMOVAL) merged into one
    // JSON array of result items, ready for RevitLinkChangeClassifier.Classify.
    Task<JsonElement> GetDiffAsync(string accessToken, string elementGroupId, int startVersion, string? region, CancellationToken cancellationToken = default);
}

public record AecProject(string Id, string Name, string HubId);

public record AecElementGroup(string Id, string? Name, string? FileUrn, string? FileVersionUrn, int? VersionNumber);
