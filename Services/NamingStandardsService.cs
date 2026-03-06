using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ApsSamples.Services;

public interface INamingStandardsService
{
    Task<NamingStandard?> GetNamingStandardAsync(string accessToken, string projectId, string namingStandardId);
    Task<NamingStandard?> GetFolderNamingStandardAsync(string accessToken, string projectId, string folderId);
    string GenerateModelName(NamingStandard standard, Dictionary<string, string> fieldValues);
    List<string> GenerateModelNames(NamingStandard standard, List<Dictionary<string, string>> fieldValuesList);
}

public class NamingStandardsService(IHttpClientFactory httpClientFactory, ILogger<NamingStandardsService> logger) : INamingStandardsService
{
    public async Task<NamingStandard?> GetNamingStandardAsync(string accessToken, string projectId, string namingStandardId)
    {
        try
        {
            var httpClient = httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var url = $"https://developer.api.autodesk.com/bim360/docs/v1/projects/{projectId}/naming-standards/{namingStandardId}";

            logger.LogInformation("Calling ACC Naming Standards API: {Url}", url);

            var response = await httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                logger.LogError("ACC Naming Standards API error: {StatusCode} - {Content}", response.StatusCode, errorContent);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            return JsonSerializer.Deserialize<NamingStandard>(content, options);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting naming standard {Id} for project {ProjectId}", namingStandardId, projectId);
            return null;
        }
    }

    public async Task<NamingStandard?> GetFolderNamingStandardAsync(string accessToken, string projectId, string folderId)
    {
        try
        {
            var httpClient = httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            // Step 1: Get folder details to extract naming standard ID
            var folderUrl = $"https://developer.api.autodesk.com/data/v1/projects/{projectId}/folders/{folderId}";
            
            logger.LogInformation("Getting folder details: {Url}", folderUrl);

            var folderResponse = await httpClient.GetAsync(folderUrl);

            if (!folderResponse.IsSuccessStatusCode)
            {
                var errorContent = await folderResponse.Content.ReadAsStringAsync();
                logger.LogError("Error getting folder details: {StatusCode} - {Content}", folderResponse.StatusCode, errorContent);
                return null;
            }

            var folderContent = await folderResponse.Content.ReadAsStringAsync();
            
            // Parse folder data to get naming standard IDs
            var folderData = JsonSerializer.Deserialize<JsonElement>(folderContent);
            
            if (!folderData.TryGetProperty("data", out var data))
            {
                logger.LogWarning("No data property found in folder response");
                return null;
            }

            if (!data.TryGetProperty("attributes", out var attributes))
            {
                logger.LogWarning("No attributes property found in folder data");
                return null;
            }

            if (!attributes.TryGetProperty("extension", out var extension))
            {
                logger.LogInformation("No extension property found in folder attributes - no naming standard applied");
                return null;
            }

            if (!extension.TryGetProperty("data", out var extensionData))
            {
                logger.LogInformation("No data property found in extension - no naming standard applied");
                return null;
            }

            if (!extensionData.TryGetProperty("namingStandardIds", out var namingStandardIds))
            {
                logger.LogInformation("No namingStandardIds found for folder {FolderId} - no naming standard applied", folderId);
                return null;
            }

            // Get the first naming standard ID (folders can have multiple, but we'll use the first one)
            string? namingStandardId = null;
            
            if (namingStandardIds.ValueKind == JsonValueKind.Array && namingStandardIds.GetArrayLength() > 0)
            {
                namingStandardId = namingStandardIds[0].GetString();
            }
            else if (namingStandardIds.ValueKind == JsonValueKind.String)
            {
                namingStandardId = namingStandardIds.GetString();
            }

            if (string.IsNullOrEmpty(namingStandardId))
            {
                logger.LogInformation("No naming standard ID found in namingStandardIds array for folder {FolderId}", folderId);
                return null;
            }

            logger.LogInformation("Found naming standard ID: {NamingStandardId} for folder {FolderId}", namingStandardId, folderId);
            
            return await GetNamingStandardAsync(accessToken, projectId, namingStandardId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting naming standard for folder {FolderId} in project {ProjectId}", folderId, projectId);
            return null;
        }
    }

    public string GenerateModelName(NamingStandard standard, Dictionary<string, string> fieldValues)
    {
        if (standard?.Definition == null || standard.Definition.Fields == null)
        {
            logger.LogWarning("Cannot generate model name: invalid naming standard");
            return string.Empty;
        }

        var delimiter = standard.Definition.Delimiter ?? "-";
        var parts = new List<string>();

        // Build the model name using fields in order
        foreach (var field in standard.Definition.Fields.OrderBy(f => f.AttributeId))
        {
            var fieldName = field.Name ?? string.Empty;
            
            if (fieldValues.TryGetValue(fieldName, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                parts.Add(value);
            }
            else if (!string.IsNullOrWhiteSpace(field.DefaultValue))
            {
                parts.Add(field.DefaultValue);
            }
            else if (!field.Optional)
            {
                logger.LogWarning("Required field {FieldName} has no value", fieldName);
                // Use placeholder for required fields
                parts.Add($"{{{fieldName}}}");
            }
        }

        var modelName = string.Join(delimiter, parts);

        // Add .rvt extension if not present
        if (!modelName.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase))
        {
            modelName += ".rvt";
        }

        logger.LogInformation("Generated model name: {ModelName} from naming standard: {StandardName}", modelName, standard.Name);

        return modelName;
    }

    public List<string> GenerateModelNames(NamingStandard standard, List<Dictionary<string, string>> fieldValuesList)
    {
        var modelNames = new List<string>();

        foreach (var fieldValues in fieldValuesList)
        {
            var modelName = GenerateModelName(standard, fieldValues);
            if (!string.IsNullOrEmpty(modelName))
            {
                modelNames.Add(modelName);
            }
        }

        return modelNames;
    }
}

// Response models based on actual BIM 360 Docs API response
public class NamingStandard
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("definition")]
    public NamingStandardDefinition? Definition { get; set; }

    // These properties may not be in the BIM 360 API response, but are needed for UI compatibility
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("isDefault")]
    public bool IsDefault { get; set; }

    // Computed property for backward compatibility
    [JsonIgnore]
    public string? Template => Definition != null 
        ? GenerateTemplate(Definition.Fields, Definition.Delimiter) 
        : null;

    // Computed property - maps definition.fields to Fields for backward compatibility
    [JsonIgnore]
    public List<NamingStandardField>? Fields => Definition?.Fields;

    [JsonPropertyName("createdAt")]
    public DateTime? CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime? UpdatedAt { get; set; }

    [JsonPropertyName("createdBy")]
    public string? CreatedBy { get; set; }

    [JsonPropertyName("updatedBy")]
    public string? UpdatedBy { get; set; }

    private static string GenerateTemplate(List<NamingStandardField>? fields, string? delimiter)
    {
        if (fields == null || !fields.Any())
            return string.Empty;

        var delim = delimiter ?? "-";
        var fieldPlaceholders = fields.Select(f => $"{{{f.Name}}}");
        return string.Join(delim, fieldPlaceholders);
    }
}

public class NamingStandardDefinition
{
    [JsonPropertyName("fields")]
    public List<NamingStandardField>? Fields { get; set; }

    [JsonPropertyName("metadata")]
    public List<NamingStandardField>? Metadata { get; set; }

    [JsonPropertyName("delimiter")]
    public string? Delimiter { get; set; }
}

public class NamingStandardField
{
    [JsonPropertyName("attributeId")]
    public int AttributeId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("optional")]
    public bool Optional { get; set; }

    // Computed property for backward compatibility
    [JsonIgnore]
    public bool Required => !Optional;

    [JsonPropertyName("options")]
    public List<NamingStandardFieldOption>? Options { get; set; }

    // Computed property - maps options to simple Values list for backward compatibility
    [JsonIgnore]
    public List<string>? Values => Options?.Select(o => o.Value ?? string.Empty).ToList();

    [JsonPropertyName("defaultValue")]
    public string? DefaultValue { get; set; }

    [JsonPropertyName("maxLength")]
    public int? MaxLength { get; set; }

    [JsonPropertyName("minLength")]
    public int? MinLength { get; set; }

    // Computed property for display order (use attributeId if no explicit order)
    [JsonIgnore]
    public int Order => AttributeId;

    // Computed property - use name as Id for backward compatibility
    [JsonIgnore]
    public string? Id => Name;
}

public class NamingStandardFieldOption
{
    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
