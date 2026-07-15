using Autodesk.Construction.AccountAdmin;

namespace ApsSamples.Services;

public static class RoleNames
{
    public const string BimManager = "BIM Manager";
}

public class ProjectRoleService(AdminClient adminClient, ILogger<ProjectRoleService> logger) : IProjectRoleService
{
    private readonly Dictionary<string, IReadOnlyList<string>> _cache = new();

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
}
