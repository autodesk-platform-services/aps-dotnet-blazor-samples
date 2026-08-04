using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using ApsSamples.Models;
using Autodesk.Construction.AccountAdmin;

namespace ApsSamples.Services;

public static class RoleNames
{
    public const string BimManager = "BIM Manager";
    public const string ProjectAdmin = "Project Admin";
}

public class ProjectRoleService(
    AdminClient adminClient,
    IHttpClientFactory httpClientFactory,
    ILogger<ProjectRoleService> logger) : IProjectRoleService
{
    private readonly Dictionary<string, IReadOnlyList<string>> _cache = new();
    private readonly Dictionary<string, IReadOnlyList<ProjectRoleInfo>> _allRolesCache = new();

    public async Task<IReadOnlyList<string>> GetProjectRolesAsync(string projectId, string userId, string accessToken)
    {
        if (_cache.TryGetValue(projectId, out var cached))
        {
            return cached;
        }

        try
        {
            // Account Admin project IDs drop the "b." hub prefix used by the Data Management API.
            var accountProjectId = projectId.StartsWith("b.", StringComparison.OrdinalIgnoreCase)
                ? projectId[2..]
                : projectId;

            var projectUser = await adminClient.GetProjectUserAsync(
                projectId: accountProjectId,
                userId: userId,
                accessToken: accessToken);

            var roles = projectUser?.Roles?
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

    public async Task<bool> IsBimManagerAsync(string projectId, string userId, string accessToken)
    {
        var roles = await GetProjectRolesAsync(projectId, userId, accessToken);
        return roles.Any(r => string.Equals(r, RoleNames.BimManager, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<bool> IsProjectAdminAsync(string projectId, string userId, string accessToken)
    {
        var roles = await GetProjectRolesAsync(projectId, userId, accessToken);
        return roles.Any(r => string.Equals(r, RoleNames.ProjectAdmin, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<ProjectRoleInfo>> GetAllProjectRolesAsync(string projectId, string accessToken)
    {
        if (_allRolesCache.TryGetValue(projectId, out var cached))
        {
            return cached;
        }

        try
        {
            var accountProjectId = projectId.StartsWith("b.", StringComparison.OrdinalIgnoreCase)
                ? projectId[2..]
                : projectId;

            var project = await adminClient.GetProjectAsync(
                projectId: accountProjectId,
                accessToken: accessToken);

            var accountId = project?.AccountId;
            if (string.IsNullOrWhiteSpace(accountId))
            {
                logger.LogWarning("Project {ProjectId} has no AccountId; cannot fetch roles.", projectId);
                _allRolesCache[projectId] = Array.Empty<ProjectRoleInfo>();
                return _allRolesCache[projectId];
            }

            var httpClient = httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var url = $"https://developer.api.autodesk.com/hq/v2/accounts/{accountId}/projects/{accountProjectId}/industry_roles";
            var response = await httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                logger.LogError("Industry roles API error for project {ProjectId}: {StatusCode} - {Content}", projectId, response.StatusCode, errorContent);
                _allRolesCache[projectId] = Array.Empty<ProjectRoleInfo>();
                return _allRolesCache[projectId];
            }

            var content = await response.Content.ReadAsStringAsync();
            var roles = JsonSerializer.Deserialize<List<IndustryRoleDto>>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            }) ?? new List<IndustryRoleDto>();

            var mapped = roles
                .Where(r => !string.IsNullOrWhiteSpace(r.Id) && !string.IsNullOrWhiteSpace(r.Name))
                .Select(r => new ProjectRoleInfo { RoleId = r.Id!, RoleName = r.Name! })
                .ToList();

            _allRolesCache[projectId] = mapped;
            return mapped;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching all project roles for project {ProjectId}", projectId);
            _allRolesCache[projectId] = Array.Empty<ProjectRoleInfo>();
            return _allRolesCache[projectId];
        }
    }

    private class IndustryRoleDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}
