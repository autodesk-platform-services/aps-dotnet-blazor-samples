using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ApsSamples.Services;

public interface IAccLinkedFilesService
{
    Task<AccLinkedFilesResponse?> GetLinkedFilesAsync(string accessToken, string projectId, string versionUrn);
}

public class AccLinkedFilesService(IHttpClientFactory httpClientFactory, ILogger<AccLinkedFilesService> logger) : IAccLinkedFilesService
{
    public async Task<AccLinkedFilesResponse?> GetLinkedFilesAsync(string accessToken, string projectId, string versionUrn)
    {
        try
        {
            var httpClient = httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            // URL-encode the version URN
            var encodedVersionUrn = Uri.EscapeDataString(versionUrn);

            // ACC RCM API endpoint - explicitly set includeHost=false to exclude host file
            var url = $"https://developer.api.autodesk.com/construction/rcm/v1/projects/{projectId}/published-versions/{encodedVersionUrn}/linked-files?includeHost=false";

            logger.LogInformation("Calling ACC RCM API: {Url}", url);

            var response = await httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                logger.LogError("ACC RCM API error: {StatusCode} - {Content}", response.StatusCode, errorContent);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            return JsonSerializer.Deserialize<AccLinkedFilesResponse>(content, options);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error calling ACC RCM API for project {ProjectId}, version {VersionUrn}", projectId, versionUrn);
            return null;
        }
    }
}

// Response models based on ACC RCM API documentation
public class AccLinkedFilesResponse
{
    [JsonPropertyName("linkedFiles")]
    public LinkedFilesContainer? LinkedFiles { get; set; }
}

public class LinkedFilesContainer
{
    [JsonPropertyName("pagination")]
    public Pagination? Pagination { get; set; }

    [JsonPropertyName("results")]
    public List<LinkedFile>? Results { get; set; }
}

public class Pagination
{
    [JsonPropertyName("limit")]
    public int Limit { get; set; }

    [JsonPropertyName("offset")]
    public int Offset { get; set; }

    [JsonPropertyName("nextUrl")]
    public string? NextUrl { get; set; }

    [JsonPropertyName("nextOffset")]
    public int? NextOffset { get; set; }

    [JsonPropertyName("totalResults")]
    public int TotalResults { get; set; }
}

public class LinkedFile
{
    [JsonPropertyName("modelName")]
    public string? ModelName { get; set; }

    [JsonPropertyName("signedUrl")]
    public string? SignedUrl { get; set; }

    [JsonPropertyName("itemId")]
    public string? ItemId { get; set; }

    [JsonPropertyName("versionId")]
    public string? VersionId { get; set; }

    [JsonPropertyName("publishStatus")]
    public string? PublishStatus { get; set; }
}
