using System.Text.Json;

namespace ApsSamples.Services;

public interface IAecDataModelService
{
    // Extraction status of the tip version of a model (fileUrn is the Data Management lineage URN).
    Task<AecExtractionStatus> GetExtractionStatusAsync(string accessToken, string fileUrn, string accProjectId, string? region, CancellationToken cancellationToken = default);

    // Ids of the RVT Links elements (Types and Instances) in the tip version of the element group.
    Task<IReadOnlyList<string>> GetLinkElementIdsAsync(string accessToken, string elementGroupId, string? region, CancellationToken cancellationToken = default);

    // All pages of diffElementGroupByVersionWithLatest (ADDITION and REMOVAL) merged into one
    // JSON array of result items, ready for RevitLinkChangeClassifier.Classify.
    Task<JsonElement> GetDiffAsync(string accessToken, string elementGroupId, int startVersion, string? region, CancellationToken cancellationToken = default);
}

public record AecExtractionStatus(string? Status, string? ElementGroupId, int? VersionNumber)
{
    public bool IsSuccess => string.Equals(Status, "SUCCESS", StringComparison.OrdinalIgnoreCase);
}
