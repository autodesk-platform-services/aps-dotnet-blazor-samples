using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ApsSamples.Services;

public static class RoleNames
{
    public const string BimManager = "BIM Manager";
}

public class ProjectRoleService(IHttpClientFactory httpClientFactory, ILogger<ProjectRoleService> logger) : IProjectRoleService
{
    private readonly Dictionary<string, IReadOnlyList<string>> _cache = new();

    public async Task<IReadOnlyList<string>> GetProjectRolesAsync(string projectId, string accessToken)
    {
        if (_cache.TryGetValue(projectId, out var cached))
        {
            return cached;
        }

        try
        {
            var httpClient = httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var url = $"https://developer.api.autodesk.com/construction/admin/v2/projects/{projectId}/users/me";

            var response = await httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                logger.LogWarning("ACC AccountAdmin /users/me failed for project {ProjectId}: {StatusCode} - {Content}",
                    projectId, response.StatusCode, errorContent);
                _cache[projectId] = Array.Empty<string>();
                return _cache[projectId];
            }

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var payload = JsonSerializer.Deserialize<ProjectUserMeResponse>(content, options);

            var roles = payload?.Roles?
                .Select(r => r.Name ?? string.Empty)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToList() ?? new List<string>();

            _cache[projectId] = roles;
            return roles;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching project roles for project {ProjectId}", projectId);
            _cache[projectId] = Array.Empty<string>();
            return _cache[projectId];
        }
    }

    public async Task<bool> IsBimManagerAsync(string projectId, string accessToken)
    {
        var roles = await GetProjectRolesAsync(projectId, accessToken);
        return roles.Any(r => string.Equals(r, RoleNames.BimManager, StringComparison.OrdinalIgnoreCase));
    }

    private class ProjectUserMeResponse
    {
        [JsonPropertyName("roles")]
        public List<ProjectUserRole>? Roles { get; set; }
    }

    private class ProjectUserRole
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}
